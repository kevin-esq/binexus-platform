# Orders bounded context

Status: **active** (Phase 1).

Domain reference: [`docs/domains/orders.md`](../../../../../docs/domains/orders.md).

Orders is the first real business workflow. It owns order state, order lines, approvals, cancellations, and transition audit. It emits events for Inventory, Warehouse, Logistics, Billing, and Reporting to react.

Implemented first slice:

```txt
CreateOrderCommand
↓
Order + OrderLine persistence
↓
ORDER_CREATED recorded in outbox
↓
POST /orders
```

Current structure:

```txt
orders/
├── orders.module.ts
├── application/
│   └── commands/
│       └── create-order.command.ts
└── presentation/
    └── orders.controller.ts
```
