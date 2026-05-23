# Bounded contexts

Binexus is organized as a modular monolith with five bounded contexts. They live under [`apps/backend/src/contexts/`](../../apps/backend/src/contexts/).

| Context     | Phase | Responsibility                                           |
| ----------- | ----- | -------------------------------------------------------- |
| `identity`  | 0     | Tenants, users, branches, auth (JWT), RBAC               |
| `orders`    | 1     | Order lifecycle, approvals, state machine                |
| `inventory` | 2     | Stock per branch, reservations, movements, transfers     |
| `sales`     | 5     | POS retail and restaurant, tickets, payment registration |
| `logistics` | 4-6   | Warehouse (lite), route building, liquidation            |

## Rules of engagement

1. **No direct service calls across contexts.** If `Sales` needs `Inventory` to reserve stock, it publishes a domain event. The other context's handler reacts.
2. **Each context owns its tables.** Cross-context joins in Prisma are forbidden. Use events to project read models if you need them.
3. **Shared types live in `@binexus/types`.** Anything imported across contexts is intentional and reviewed.
4. **Commands stay inside their context.** A command handler in `orders` cannot dispatch a command from `inventory`. Use events.
5. **`identity` is the only context other contexts may query directly** (for user/branch lookups during authorization). This is a deliberate exception.

## Per-context structure (when implemented)

```
<context>/
├── <context>.module.ts
├── domain/                  Entities, value objects, state machines (pure TS)
├── application/             Commands, queries, handlers
├── infrastructure/          Prisma repositories, external integrations
└── presentation/            HTTP controllers (or other transport)
```

In Phase 0 only `identity` has the full shape. The others are README placeholders.

## Future extraction

A context graduates to a separate service in `services/` only when at least one is true:

- It needs a different scaling profile (e.g. high-throughput event consumer).
- It needs a different runtime (e.g. Rust for tight loops).
- It has an independent failure domain we want to isolate (e.g. POS must keep selling if the analytics service crashes).

Until then: stay in the monolith.
