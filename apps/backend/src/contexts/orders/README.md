# Orders bounded context

Status: **placeholder** (Phase 1 - next).

Domain reference: [`docs/domains/orders.md`](../../../../../docs/domains/orders.md).

Orders is the first real business workflow. It owns order state, order lines, approvals, cancellations, and transition audit. It emits events for Inventory, Warehouse, Logistics, Billing, and Reporting to react.

Planned first slice:

```txt
CreateOrderCommand
↓
ApproveOrderCommand
↓
ORDER_APPROVED
↓
Inventory reservation
↓
Warehouse picking task
```

Planned structure:

```txt
orders/
├── orders.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
