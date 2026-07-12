# Commands and use cases

## Decision

Every write use case is a **named command** handled inside its module (MediatR / feature handlers in .NET). Reads use query services. Nest `@nestjs/cqrs` is historical — superseded by [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md) (see also superseded [ADR-0007](../adr/0007-command-bus-cqrs-lite.md)).

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL.

## Why

- **Explicit use cases.** Named commands document what the system does.
- **Single dispatch surface.** HTTP endpoints, workers, and tests invoke the same handlers.
- **Easy to wrap.** Logging, tenancy, idempotency, and outbox recording sit at the handler / unit-of-work boundary.

## Pieces (.NET)

| Piece                      | Where                                                                                                                                 |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Module handlers / services | [`apps/backend/src/Modules/Binexus.Modules.*/`](../../apps/backend/src/Modules/)                                                      |
| Cross-module contracts     | e.g. `IInventoryReservationApi` ([ADR-0014](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md))                         |
| Tenant context             | [`AuthenticatedTenantMiddleware`](../../apps/backend/src/Binexus.Platform/Tenancy/AuthenticatedTenantMiddleware.cs), `ICurrentTenant` |
| Outbox staging             | Same EF `SaveChangesAsync` as the aggregate write                                                                                     |

## Command metadata

Handlers accept correlation / causation / idempotency where the HTTP contract requires it:

- `Idempotency-Key` (or module-specific store) for open/create/close style endpoints.
- `correlationId` / `causationId` on outbox envelopes for tracing.

## Rules

- Controllers map HTTP → command DTO → handler. No business logic in controllers.
- Persist domain change and outbox row in **one** transaction.
- Cross-module work: published contract in the same TX, or integration event via outbox — never a silent direct DbContext poke into another module's tables.
