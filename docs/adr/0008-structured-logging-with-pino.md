# ADR-0008: Structured logging with Pino + per-request context

| Field    | Value                                      |
| -------- | ------------------------------------------ |
| Status   | Superseded by ADR-0015                     |
| Date     | 2026-05-23                                 |
| Deciders | Kevin Esquivel                             |
| Tags     | observability, logging, debuggability, ops |

## Context and problem statement

The system is event-driven, eventually-consistent (ADR-0004), multi-tenant (ADR-0005), and will one day run on offline-first hubs that sync to the cloud (ADR-0003). Debugging across hops without **structured, correlated logs** is hopeless.

Unstructured logs (`console.log("user X did Y")`) make it impossible to:

- Aggregate by tenant or user.
- Trace a single command through controller → handler → outbox → dispatcher → subscriber.
- Filter noise in production.

**Question:** what logger, what schema, and what request context do we standardize on?

## Decision drivers

- **Structured JSON** — machine-readable for any aggregator (Loki, Datadog, ELK, plain `jq`).
- **Per-request context** — `requestId`, `tenantId`, `userId`, `role`, `branchId` automatically attached.
- **Low overhead** — Node loggers vary wildly in throughput.
- **Pretty output in dev** — JSON in prod, but readable in `pnpm dev`.
- **Plays well with NestJS** — Nest's built-in logger should be replaced cleanly.

## Considered options

1. **`console.log` everywhere.**
2. **Winston** with custom transports.
3. **NestJS built-in logger** (default).
4. **Pino via `nestjs-pino`** — fastest JSON logger in Node, with NestJS integration.

## Decision outcome

**Chosen option:** _Pino via `nestjs-pino`_, integrated with our `TenantContextMiddleware` so every log line emitted during a request automatically carries `{ requestId, tenantId, userId, role, branchId }`.

Concretely:

- All logs are JSON in production.
- `pino-pretty` is used in development for human-readable output.
- The HTTP layer logs request start, request end (with status + duration), and unhandled exceptions.
- `HttpExceptionFilter` enriches error logs with the command name (if any), the stack, and the user/tenant context.
- Sensitive fields (`password`, `refreshToken`, `Authorization` header) are redacted via Pino's `redact` config.

### Positive consequences

- **Tracing a request** across handler, outbox, and dispatcher is `grep '"requestId":"01H..."'` — done.
- **Tenant isolation in observability** comes for free — filter any dashboard by `tenantId`.
- **Performance** — Pino is ~5× faster than Winston in benchmarks; logging is not a hot-path bottleneck.
- **Redaction** is centralized — no scattered `delete data.password` calls.

### Negative consequences

- Developers must use the injected logger, not `console.log`. We enforce via ESLint (`no-console`).
- Pino's pretty mode is dev-only — production logs are JSON, harder to eyeball without `pino-pretty | less`.
- Trace propagation across the eventual Redis Streams transport (ADR-0004) requires us to carry `requestId` / `correlationId` in the envelope — already part of the envelope contract.

### Trade-offs accepted

- We don't yet ship distributed tracing (OpenTelemetry). Logs + correlation IDs are sufficient for Phase 0; we add OTel when the cost of "stitching logs manually" exceeds the cost of running a collector.

## Pros and cons of the options

### Option 1 — `console.log`

- **Good:** Zero setup.
- **Bad:** Unstructured.
- **Bad:** No correlation IDs.
- **Bad:** No redaction.
- **Bad:** Disqualifies any serious aggregation.

### Option 2 — Winston

- **Good:** Mature; many transports.
- **Bad:** Slower than Pino.
- **Bad:** Transport architecture is heavier than we need.

### Option 3 — NestJS built-in logger

- **Good:** Zero new dependency.
- **Bad:** Not structured by default.
- **Bad:** No per-request context.
- **Bad:** Replacing it later is more work than choosing right now.

### Option 4 — Pino via `nestjs-pino` _(chosen)_

- **Good:** JSON-first.
- **Good:** Per-request child loggers — context just works.
- **Good:** Fastest in class.
- **Good:** Active maintenance, large community.
- **Bad:** Adds a dependency.
- **Bad:** Dev pretty-printing requires `pino-pretty` as a peer.

## Validation

This decision is working if:

- Every log line in production is valid JSON with `requestId`, `tenantId` (when present), and `userId` (when present).
- A single command can be traced end-to-end with one filter expression.
- `password`, `refreshToken`, `Authorization` headers **never** appear in logs (assert via integration test).
- `pnpm dev` produces readable, color-coded output for local debugging.

It is failing if:

- We catch `console.log` in PRs (ESLint rule firing).
- A user reports a bug and we cannot reconstruct the request from logs because the context didn't propagate (signal: another async boundary needs to be wrapped in `tenantContext.run(...)` or a child logger).
- A sensitive field shows up in production logs (signal: add it to the redact list and write a test).

## More information

- [Pino docs](https://getpino.io/)
- [`nestjs-pino`](https://github.com/iamolegga/nestjs-pino)
- [OpenTelemetry](https://opentelemetry.io/) — the planned next step when correlation IDs alone stop scaling.
- Related: ADR-0004 (outbox — envelope carries `correlationId` for trace continuity), ADR-0005 (multi-tenant — context source).
- Related docs: [`docs/architecture/observability.md`](../architecture/observability.md)
