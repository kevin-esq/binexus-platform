# Inventory bounded context

Status: **active** (Phase 2).

Domain reference: [`docs/domains/inventory.md`](../../../../../docs/domains/inventory.md).

## Implemented

```txt
inventory/
├── inventory.module.ts
├── application/
│   ├── inventory-reservation.service.ts
│   └── inventory-read.service.ts
├── events/
│   ├── order-approved-inventory.handler.ts
│   └── order-cancelled-inventory.handler.ts
└── presentation/
    └── inventory.controller.ts
```

- **Reserve** on `ORDER_APPROVED` → `INVENTORY_RESERVED` or `INVENTORY_RESERVATION_FAILED`.
- **Release** on `ORDER_CANCELLED` → `INVENTORY_RELEASED`.
- **Read** `GET /inventory/stock` for tenant-scoped stock visibility.

## Web

- `/inventory` in `apps/web` lists stock via SDK `listStockItems()`.

Planned next: `AdjustStockCommand`, transfers, movement ledger UI.
