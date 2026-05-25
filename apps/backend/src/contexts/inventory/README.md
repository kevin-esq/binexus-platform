# Inventory bounded context

Status: **active** (Phase 2 — reservation slice).

Domain reference: [`docs/domains/inventory.md`](../../../../../docs/domains/inventory.md).

Inventory owns stock balances, reservations, movements, transfers, and adjustments. It consumes order/sale facts and emits reservation/movement facts.

## Implemented

```txt
inventory/
├── inventory.module.ts
├── application/
│   └── inventory-reservation.service.ts
└── events/
    ├── order-approved-inventory.handler.ts
    └── order-cancelled-inventory.handler.ts
```

- **Reserve** on `ORDER_APPROVED` → `INVENTORY_RESERVED` or `INVENTORY_RESERVATION_FAILED`.
- **Release** on `ORDER_CANCELLED` → `INVENTORY_RELEASED`.

Planned next: `domain/`, `infrastructure/`, `presentation/` (read API, adjustments, transfers).
