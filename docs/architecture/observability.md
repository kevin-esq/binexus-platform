# Observability

## Decision

**Structured logging with Pino** via `nestjs-pino`, wired with per-request context (`requestId`, `tenantId`, `userId`, `role`). Logs are JSON in production, pretty in development.

Why early: offline-first + multi-tenant + events means debugging will be hard without structured logs from day one.

## Setup

- [`apps/backend/src/common/logger/logger.module.ts`](../../apps/backend/src/common/logger/logger.module.ts) — Pino transport, redaction, customProps.
- [`apps/backend/src/common/tenant/tenant-context.middleware.ts`](../../apps/backend/src/common/tenant/tenant-context.middleware.ts) — attaches `requestId`, `tenantId`, `userId`, `role` to the request so Pino picks them up.

## What's redacted

- `req.headers.authorization`
- `req.headers.cookie`
- any field named `password`

## Conventions

| Level   | Use for                                                 |
| ------- | ------------------------------------------------------- |
| `error` | Unhandled exceptions, 5xx, infra failures               |
| `warn`  | Recoverable issues, expired tokens, retried operations  |
| `info`  | Lifecycle events, completed commands, successful events |
| `debug` | Per-request detail, event dispatch traces               |
| `trace` | Never enabled in prod                                   |

## What to log in business code

- **Command execution**: `{ command: 'CreateOrder', durationMs, ok: true }`.
- **Event dispatch**: `{ event: 'ORDER_CREATED', eventId, correlationId }`.
- **External calls**: `{ target: 'minio', op: 'putObject', status }`.

## What NOT to log

- Plaintext passwords / tokens (redacted automatically but never compose them either).
- Entire request bodies for non-debug levels.
- PII beyond `userId` / `email` (no full names, addresses, payment data in plain logs).

## Future

- Phase 1+: ship traces via OpenTelemetry → Tempo/Jaeger.
- Phase 2+: metrics via OpenTelemetry → Prometheus.
- Phase 5+: log aggregation in Loki or similar; alerts on `error` rate per tenant.
