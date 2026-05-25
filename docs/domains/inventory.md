# Inventory domain

Status: **active** (Phase 2 — reservation slice). Bounded context: `inventory`.

Inventory owns the truth of stock: what exists, where it exists, what is reserved, and how it moved. It does not own picking tasks or route dispatch.

## Owns

- `StockItem` — on-hand and reserved quantities per tenant/branch/product. Available = `onHand - reserved` (computed in application code).
- `StockReservation` — quantity promised to an order line (`ACTIVE | RELEASED | FAILED`).
- `StockMovement` — immutable movement ledger (`RESERVE`, `RELEASE`, `ADJUSTMENT`).
- `StockTransfer` — branch-to-branch movement request (planned).
- `StockAdjustment` — manual correction with reason (planned).

## Does not own

- Product definitions. Those belong to [`catalog`](catalog.md).
- Order state. That belongs to [`orders`](orders.md).
- Picking execution. That belongs to [`warehouse`](warehouse.md).
- Sales ticket lifecycle. That belongs to [`sales`](sales.md).

## Commands

Implemented indirectly via event handlers (no HTTP API in this slice):

- **Reserve on approve** — `OrderApprovedInventoryHandler` reacts to `ORDER_APPROVED`.
- **Release on cancel** — `OrderCancelledInventoryHandler` reacts to `ORDER_CANCELLED`.

Planned explicit commands:

- `CommitReservationCommand`.
- `RecordStockMovementCommand`.
- `CreateStockTransferCommand`.
- `AdjustStockCommand`.

## Events emitted

| Event                          | When                                                          |
| ------------------------------ | ------------------------------------------------------------- |
| `INVENTORY_RESERVED`           | All order lines reserved successfully after `ORDER_APPROVED`. |
| `INVENTORY_RESERVATION_FAILED` | Any line lacks available stock; no partial reservation.       |
| `INVENTORY_RELEASED`           | Active reservations released after `ORDER_CANCELLED`.         |

Planned: `STOCK_MOVED`, `STOCK_ADJUSTED`.

## Events consumed

| Event             | Handler                          | Behavior                                                |
| ----------------- | -------------------------------- | ------------------------------------------------------- |
| `ORDER_APPROVED`  | `OrderApprovedInventoryHandler`  | Reserve stock for every line; write movements + outbox. |
| `ORDER_CANCELLED` | `OrderCancelledInventoryHandler` | Release active reservations; write movements + outbox.  |

Planned: `SALE_CREATED`, `PICKING_COMPLETED`.

## Rules (reservation slice)

1. **No negative stock by default** — reservation succeeds only when `onHand - reserved >= quantity` for every line.
2. **All-or-nothing** — if any line fails, no stock is reserved; emit `INVENTORY_RESERVATION_FAILED` and mark line reservations `FAILED`.
3. **Idempotency** — unique `(tenantId, orderId, orderLineId)` on `StockReservation`; re-delivery of `ORDER_APPROVED` with existing `ACTIVE` lines is a no-op; failure and release paths are similarly idempotent.
4. **Tenant context in handlers** — event handlers run work inside `TenantContextService.run()` using `event.tenantId` and the acting user from the payload.
5. **Outbox** — inventory outcome events are recorded in the same DB transaction as stock changes.

## Implementation layout

```
apps/backend/src/contexts/inventory/
  application/inventory-reservation.service.ts
  events/order-approved-inventory.handler.ts
  events/order-cancelled-inventory.handler.ts
  inventory.module.ts
```

## Allowed dependencies

- May reference `productId` from order lines but cannot mutate catalog data.
- May emit events that Warehouse will consume later.
- Must not decide commercial order state. It reports reservation outcomes only.

## Open questions

- Do we support batch/lot/expiry tracking in Phase 2 or defer?
- Should reservations be hard (blocking) or soft (advisory) by tenant setting?
- ~~Should `Orders` compensate on `INVENTORY_RESERVATION_FAILED`?~~ **Resolved:** `orders` auto-cancels (`APPROVED` → `CANCELLED`) via tenant system user.
