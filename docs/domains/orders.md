# Orders domain (Phase 1 — next)

This is the first business module to implement after Phase 0. Everything else (inventory reservations, picking, routes, deliveries, liquidations, billing) hangs off the order lifecycle. We implement Orders **before** POS because POS is just one of several ways to create an order.

## Planned entities (sketch — not implemented yet)

```
Order
  id, tenantId, branchId, customerId, state (OrderState)
  totalCents, currency, createdAt, createdBy

OrderLine
  id, orderId, productId, qty, unitPriceCents

OrderTransition
  id, orderId, from, to, byUserId, atTimestamp, reason
```

## State machine

See [`states/order.md`](../states/order.md). Single source of truth for which transitions are legal — enforced by `canTransition()` in [`packages/types/src/orders.ts`](../../packages/types/src/orders.ts).

## Planned commands

- `CreateOrderCommand`
- `ApproveOrderCommand`
- `CancelOrderCommand`
- `AssignWarehouseCommand`
- `MarkPickingCompleteCommand`
- `DispatchRouteCommand`
- `ConfirmDeliveryCommand`
- `LiquidateOrderCommand`

## Planned events (already registered in `packages/events`)

- `ORDER_CREATED`
- `ORDER_APPROVED`
- `ORDER_CANCELLED`

Additional events will be added per phase: `ORDER_DISPATCHED`, `ORDER_DELIVERED`, `ORDER_SETTLED`.

## Open questions to resolve before coding

1. Are prices captured at order creation (snapshot) or evaluated at fulfillment?
2. Is there a credit check step between `DRAFT → APPROVED` for B2B tenants?
3. Multi-branch order: same order fulfilled from two branches? Or split into two orders?

Answer them in this doc when Phase 1 starts.
