# Bounded contexts

Binexus is organized as a modular monolith with ten initial bounded contexts. They live under [`apps/backend/src/contexts/`](../../apps/backend/src/contexts/).

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
| `sales`     | F5    | Planned        | POS retail and restaurant, tickets, payment registration                    |
| `billing`   | F7    | Planned        | Invoices, fiscal documents, payment allocation, receivables                 |
| `reporting` | F8+   | Planned        | Read models, operational dashboards, analytics projections                  |

## Rules of engagement

1. **No direct service calls across contexts.** If `Sales` needs `Inventory` to reserve stock, it publishes a domain event. The other context's handler reacts.
2. **Each context owns its tables.** Cross-context joins in Prisma are forbidden. Use events to project read models if you need them.
3. **Shared types live in `@binexus/types`.** Anything imported across contexts is intentional and reviewed.
4. **Commands stay inside their context.** A command handler in `orders` cannot dispatch a command from `inventory`. Use events.
5. **`identity` is the only context other contexts may query directly** (for user/branch lookups during authorization). This is a deliberate exception.
6. **`catalog` and `customers` are reference contexts.** Other contexts may store immutable snapshots from them, but not mutate their rows.
7. **`reporting` never owns source-of-truth writes.** It consumes events and builds projections.

## Per-context structure (when implemented)

```
<context>/
├── <context>.module.ts
├── domain/                  Entities, value objects, state machines (pure TS)
├── application/             Commands, queries, handlers
├── infrastructure/          Prisma repositories, external integrations
└── presentation/            HTTP controllers (or other transport)
```

Five bounded contexts are registered in `AppModule` and implemented beyond README placeholders: `identity`, `orders`, `inventory`, `warehouse`, and `logistics`. The remaining contexts (`catalog`, `customers`, `sales`, `billing`, `reporting`) still have folder stubs only.

Implemented contexts follow the structure below (some omit empty `domain/` or `infrastructure/` folders until needed):

## Future extraction

A context graduates to a separate service in `services/` only when at least one is true:

- It needs a different scaling profile (e.g. high-throughput event consumer).
- It needs a different runtime (e.g. Rust for tight loops).
- It has an independent failure domain we want to isolate (e.g. POS must keep selling if the analytics service crashes).

Until then: stay in the monolith.
