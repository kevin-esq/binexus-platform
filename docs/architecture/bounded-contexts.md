# Bounded contexts

Binexus is organized as a modular monolith with ten initial bounded contexts. They live under [`apps/backend/src/Modules/`](../../apps/backend/src/Modules/) (one assembly per module). See [`dotnet-backend.md`](./dotnet-backend.md).

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL. NestJS is not a supported option. Legacy backend: NestJS, removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md) migration.

The contexts intentionally map 1:1 with the operational domains documented in [`docs/domains`](../domains/). This keeps ownership clear while the product is still learning the business. We can merge or split later with a new ADR, but Phase 1 starts with explicit boundaries.

| Context     | Phase | Status in repo | Responsibility                                                              |
| ----------- | ----- | -------------- | --------------------------------------------------------------------------- |
| `identity`  | F0    | Active         | Tenants, users, branches, auth (JWT), RBAC                                  |
| `catalog`   | F1+   | Planned        | Products, SKUs, units, price lists, tax categories                          |
| `customers` | F1+   | Planned        | Customers, billing identity, addresses, credit profile                      |
| `orders`    | F1    | Active         | Order lifecycle, approvals, state machine                                   |
| `inventory` | F2    | Active         | Stock per branch, reservations, movements, transfers                        |
| `warehouse` | F3    | Active         | Picking, packing, staging, warehouse-lite operational execution             |
| `logistics` | F4    | Active         | Delivery routes, dispatch handoff, delivery confirmation, route liquidation |
| `sales`     | F5    | Active (5.2)   | POS retail sessions, split payment (credit/delivery deferred)               |
| `billing`   | F7    | Planned        | Invoices, fiscal documents, payment allocation, receivables                 |
| `reporting` | F8+   | Planned        | Read models, operational dashboards, analytics projections                  |

**Status in repo** means the bounded context is implemented (`Active`) or still a stub (`Planned`). **Phase complete** (F1–F5) means the scoped vertical slice for that roadmap phase is shipped; contexts stay `Active` because they remain live code paths under maintenance. See the roadmap table in [`README.md`](../../README.md).

## Rules of engagement

1. **No direct domain calls across modules.** Cross-context work uses integration events (outbox/inbox) or explicit application contracts (e.g. `IInventoryReservationApi`). See [ADR-0014](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md).
2. **Each module owns its tables.** Cross-module EF joins of foreign aggregates are forbidden. Use events to project read models if you need them.
3. **Shared TypeScript types live in `@binexus/types`.** HTTP clients use `@binexus/sdk` (OpenAPI-generated). Event contracts live under `apps/backend/contracts/events` ([ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)).
4. **Commands stay inside their module.** An Orders handler does not dispatch an Inventory command; it calls a published contract or publishes an event.
5. **`identity` is the only module other modules may query directly** for user/branch lookups during authorization. This is a deliberate exception.
6. **`catalog` and `customers` are reference contexts.** Other contexts may store immutable snapshots from them, but not mutate their rows.
7. **`reporting` never owns source-of-truth writes.** It consumes events and builds projections.

## Per-module structure (when implemented)

```
Binexus.Modules.<Context>/
├── Domain/                  Aggregates, value objects, invariants
├── Application/             Commands, queries, contracts
├── Features/                Vertical slices (optional)
├── Infrastructure/          EF repositories, services
└── (registration)           Module DI + endpoint mapping via Api
```

Active modules: `Identity`, `Orders`, `Inventory`, `Warehouse`, `Logistics`, `Sales`. Planned: `Catalog`, `Customers`, `Billing`, `Reporting`.

## Future extraction

A context graduates to a separate service in `services/` only when at least one is true:

- It needs a different scaling profile (e.g. high-throughput event consumer).
- It needs a different runtime (e.g. Rust for tight loops).
- It has an independent failure domain we want to isolate (e.g. POS must keep selling if the analytics service crashes).

Until then: stay in the monolith.
