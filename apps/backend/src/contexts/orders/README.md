# Orders bounded context

Status: **placeholder** (Phase 1 — next).

## Why this context

Orders is the heart of the operational workflow. Everything else (inventory reservations, picking, routes, deliveries, liquidations, billing) hangs off the order lifecycle. We implement it first, before POS, because POS is just one of several ways to create an order.

## Planned structure

```
orders/
├── orders.module.ts
├── domain/                # Entities, value objects, OrderState machine helpers
├── application/           # Commands (CreateOrder, ApproveOrder, ...) and handlers
├── infrastructure/        # Prisma repositories
└── presentation/          # HTTP controllers
```

## Planned commands

- `CreateOrderCommand`
- `ApproveOrderCommand`
- `CancelOrderCommand`
- `AssignWarehouseCommand`
- `MarkPickingCompleteCommand`
- `DispatchRouteCommand`
- `ConfirmDeliveryCommand`
- `LiquidateOrderCommand`

## Planned events

See [`docs/states/order.md`](../../../../docs/states/order.md) for the state machine and [`docs/events/README.md`](../../../../docs/events/README.md) for the event catalog.
