# Domains

A "domain" here is the **conceptual** area of the business. A "bounded context" is the **implementation** unit (see [`architecture/bounded-contexts.md`](../architecture/bounded-contexts.md)). They usually map 1:1 but not always — e.g. Warehouse and Routes are two domains living in the single `logistics` bounded context (for now).

| Domain      | Bounded context | Phase | Status  |
| ----------- | --------------- | ----- | ------- |
| Identity    | `identity`      | 0     | Active  |
| Orders      | `orders`        | 1     | Next    |
| Inventory   | `inventory`     | 2     | Planned |
| Sales       | `sales`         | 5     | Planned |
| Warehouse   | `logistics`     | 4     | Planned |
| Routes      | `logistics`     | 5     | Planned |
| Liquidation | `logistics`     | 6     | Planned |
| Billing     | (TBD)           | 7     | Planned |

Per-domain pages:

- [`identity.md`](identity.md) — Tenant, User, Branch, Role, JWT lifecycle.
- [`orders.md`](orders.md) — Planning notes for Phase 1.
