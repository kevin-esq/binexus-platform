# Inventory domain

Status: **active** (Phase 2). Bounded context: `inventory`.

Inventory owns the truth of stock: what exists, where it exists, what is reserved, and how it moved. It does not own picking tasks or route dispatch.

## Owns

- `StockItem` — on-hand and reserved quantities per tenant/branch/product. Available = `onHand - reserved` (computed in application code).
- `StockReservation` — quantity promised to an order line (`ACTIVE | RELEASED | FAILED`).
- `StockMovement` — immutable movement ledger (`RESERVE`, `RELEASE`, `ADJUSTMENT`, `TRANSFER_OUT`, `TRANSFER_IN`, `SALE`).
- `StockTransfer` — branch-to-branch transfer request (`PENDING | RECEIVED | CANCELLED`).
- `StockAdjustment` — manual correction with reason via `AdjustStockCommand` (delta + reason).

## Does not own

- Product definitions. Those belong to [`catalog`](catalog.md).
- Order state. That belongs to [`orders`](orders.md).
- Picking execution. That belongs to [`warehouse`](warehouse.md).
- Sales ticket lifecycle. That belongs to [`sales`](sales.md).

## Commands

Implemented via event handlers:

- **Reserve on approve** — `OrderApprovedInventoryHandler` reacts to `ORDER_APPROVED`.
- **Release on cancel** — `OrderCancelledInventoryHandler` reacts to `ORDER_CANCELLED`.

## Read API

Implemented:

- `GET /inventory/stock` — tenant-scoped list of `StockItem` rows with `onHand`, `reserved`, and computed `available`.
- Query params: `branchId`, `productId`, `limit` (default 50, max 100), `cursor` (cursor pagination by `createdAt` + `id`).

Implemented explicit write commands:

- `AdjustStockCommand` — manual `onHand` correction with `StockMovement` type `ADJUSTMENT`. Rule: `nextOnHand >= reserved` (available cannot go negative against active reservations).
- **POS sale (F5.1):** `CreateSaleCommand` in `sales` decrements `onHand` inline and writes `StockMovement` type `SALE` (negative quantity). No `SALE_CREATED` inventory handler yet.
- `CreateStockTransferCommand` — creates `StockTransfer(PENDING)` and increments source `reserved` when `available >= quantity`.
- `ReceiveStockTransferCommand` — decrements source `onHand`/`reserved`, increments destination `onHand`, writes `TRANSFER_OUT` / `TRANSFER_IN` movements, marks `RECEIVED`.
- `CancelStockTransferCommand` — releases source `reserved` for pending transfers and marks `CANCELLED`.

Planned explicit write commands:

- `CommitReservationCommand`.
- `RecordStockMovementCommand`.

## Events emitted

| Event                          | When                                                             |
| ------------------------------ | ---------------------------------------------------------------- |
| `INVENTORY_RESERVED`           | All order lines reserved successfully after `ORDER_APPROVED`.    |
| `INVENTORY_RESERVATION_FAILED` | Historical Nest async compatibility only; no .NET sync producer. |
| `INVENTORY_RELEASED`           | Active reservations released after `ORDER_CANCELLED`.            |

Planned: `STOCK_MOVED`, `STOCK_ADJUSTED`.

## Events consumed

| Event             | Handler                          | Behavior                                                |
| ----------------- | -------------------------------- | ------------------------------------------------------- |
| `ORDER_APPROVED`  | `OrderApprovedInventoryHandler`  | Reserve stock for every line; write movements + outbox. |
| `ORDER_CANCELLED` | `OrderCancelledInventoryHandler` | Release active reservations; write movements + outbox.  |

Planned: `SALE_CREATED`, `PICKING_COMPLETED`.

## Rules (reservation slice)

1. **No negative stock by default** — reservation succeeds only when `onHand - reserved >= quantity` for every line.
2. **All-or-nothing** — if any line fails, no stock is reserved and sync approval returns `INSUFFICIENT_STOCK`; it does not persist `FAILED` reservations or emit `INVENTORY_RESERVATION_FAILED`.
3. **Idempotency** — unique `(tenantId, orderId, orderLineId)` on `StockReservation`; re-delivery of `ORDER_APPROVED` with existing `ACTIVE` lines is a no-op; failure and release paths are similarly idempotent.
4. **Tenant context in handlers** — event handlers run work inside `TenantContextService.run()` using `event.tenantId` and the acting user from the payload.
5. **Outbox** — inventory outcome events are recorded in the same DB transaction as stock changes.

## HTTP surface

```txt
GET /inventory/stock?branchId=&productId=&limit=50&cursor=
POST /inventory/stock/adjust
GET /inventory/stock/transfers?status=PENDING&limit=50&cursor=
POST /inventory/stock/transfers
POST /inventory/stock/transfers/:id/receive
POST /inventory/stock/transfers/:id/cancel
```

`GET` returns `{ items: StockItemSummary[], nextCursor: string | null }`. `available` is computed as `onHand - reserved` in the read service.

`POST /inventory/stock/adjust` body: `{ branchId, productId, delta, reason }`. Returns `{ stockItem, movementId }`.

- `delta` — non-zero integer; positive adds stock, negative removes.
- `reason` — 3–200 characters (trimmed).
- If no `StockItem` exists: positive `delta` creates one; negative `delta` is rejected.
- `reserved` is never modified; rejection when `onHand + delta < reserved`.

**Transfers lifecycle**

1. **Create** — `POST /inventory/stock/transfers` body `{ sourceBranchId, destinationBranchId, productId, quantity, reason? }`. Reserves `quantity` on source (`reserved += quantity`). Requires source `available >= quantity` and distinct branches.
2. **Receive** — `POST /inventory/stock/transfers/:id/receive`. Moves stock: source `onHand`/`reserved` decrease; destination `onHand` increases (creates destination item if missing). Ledger: `TRANSFER_OUT` (negative qty) at source, `TRANSFER_IN` (positive qty) at destination.
3. **Cancel** — `POST /inventory/stock/transfers/:id/cancel`. Only `PENDING`; releases source `reserved`.

`GET /inventory/stock/transfers?status=` lists transfers (default all statuses if omitted).

## Web UI

- `/inventory` — stock table with per-row **Adjust** and **Transfer** (`prompt` for destination, quantity, reason). Pending transfers section with **Receive** / **Cancel**. Links from `/orders` and home.

## Implementation layout

```
apps/backend/src/Modules/Binexus.Modules.Inventory/
  Application/
  Infrastructure/
```

## Allowed dependencies

- May reference `productId` from order lines but cannot mutate catalog data.
- May emit events that Warehouse will consume later.
- Must not decide commercial order state. It reports reservation outcomes only.

## Open questions

- Do we support batch/lot/expiry tracking in Phase 2 or defer?
- Should reservations be hard (blocking) or soft (advisory) by tenant setting?
- ~~Should `Orders` compensate on `INVENTORY_RESERVATION_FAILED`?~~ **Resolved:** `orders` auto-cancels (`APPROVED` → `CANCELLED`) via tenant system user.
