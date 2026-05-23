# Inventory bounded context

Status: **placeholder** (Phase 2).

Domain reference: [`docs/domains/inventory.md`](../../../../../docs/domains/inventory.md).

Inventory owns stock balances, reservations, movements, transfers, and adjustments. It consumes order/sale facts and emits reservation/movement facts.

Planned structure:

```txt
inventory/
├── inventory.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
