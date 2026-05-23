# Inventory domain

Status: **planned** (Phase 2). Bounded context: `inventory`.

Inventory owns the truth of stock: what exists, where it exists, what is reserved, and how it moved. It does not own picking tasks or route dispatch.

## Owns

- `StockBalance` - quantity on hand per tenant/branch/sku.
- `StockReservation` - quantity promised to an order.
- `StockMovement` - immutable movement ledger.
- `StockTransfer` - branch-to-branch movement request.
- `StockAdjustment` - manual correction with reason.

## Does not own

- Product definitions. Those belong to [`catalog`](catalog.md).
- Order state. That belongs to [`orders`](orders.md).
- Picking execution. That belongs to [`warehouse`](warehouse.md).
- Sales ticket lifecycle. That belongs to [`sales`](sales.md).

## Commands

Planned:

- `ReserveInventoryCommand`.
- `ReleaseReservationCommand`.
- `CommitReservationCommand`.
- `RecordStockMovementCommand`.
- `CreateStockTransferCommand`.
- `AdjustStockCommand`.

## Events emitted

Planned:

- `INVENTORY_RESERVED`.
- `INVENTORY_RESERVATION_FAILED`.
- `INVENTORY_RELEASED`.
- `STOCK_MOVED`.
- `STOCK_ADJUSTED`.

## Events consumed

- `ORDER_APPROVED` from Orders - reserve stock.
- `ORDER_CANCELLED` from Orders - release reservations.
- `SALE_CREATED` from Sales - decrement/commit stock for direct POS sale.
- `PICKING_COMPLETED` from Warehouse - commit picked quantities.

## Allowed dependencies

- May reference `skuId` from Catalog but cannot mutate catalog data.
- May emit events that Warehouse consumes to generate picking.
- Must not decide commercial order state. It reports reservation outcomes.

## Boundary rules

1. Inventory is a ledger, not a mutable counter hidden in product rows.
2. All stock changes are movements with reason, actor, and correlation ID.
3. Reservations are idempotent by order/line/command ID.
4. Negative stock is a tenant policy, not a silent default.

## Open questions

- Do we support batch/lot/expiry tracking in Phase 2 or defer?
- Should reservations be hard (blocking) or soft (advisory) by tenant setting?
