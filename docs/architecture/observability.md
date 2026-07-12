# Observability

## Decision

**Structured logging via ASP.NET Core** (JSON in production, readable in development), with per-request context (`requestId`, `tenantId`, `userId`, `role`). Nest `nestjs-pino` is historical — superseded by [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md) (see also superseded [ADR-0008](../adr/0008-structured-logging-with-pino.md)).

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL.

## Setup

- Api / Workers host logging configuration under `apps/backend/src/Binexus.Api` and `apps/backend/src/Binexus.Workers`.
- Tenant middleware attaches identity claims so logs can include tenant/user scope.

## Conventions

| Level   | Use for                                                 |
| ------- | ------------------------------------------------------- |
| `error` | Unhandled exceptions, 5xx, infra failures               |
| `warn`  | Recoverable issues, expired tokens, retried operations  |
| `info`  | Lifecycle events, completed commands, successful events |
| `debug` | Per-request detail, event dispatch traces               |
| `trace` | Never enabled in prod                                   |

## What to log in business code

- **Command execution**: command name, duration, success/failure code.
- **Event dispatch**: event name, event id, correlation id.
- **External calls**: MinIO / S3 op + status (never full presigned query strings in CI logs).

## What NOT to log

- Plaintext passwords / tokens.
- Entire request bodies for non-debug levels.
- PII beyond `userId` / `email` (no full names, addresses, payment data in plain logs).

## Future

- Traces via OpenTelemetry → Tempo/Jaeger.
- Metrics via OpenTelemetry → Prometheus.
- Log aggregation (Loki or similar); alerts on `error` rate per tenant.
