# Orders domain

Status: **active** (Phase 1). Bounded context: `orders`.

Orders is the first real business module. It coordinates demand before stock, warehouse, routes, billing, or POS specialize the workflow. POS is only one channel that can create an order; Orders remains channel-agnostic.

## Owns

- `Order` - tenant/branch-scoped commercial intent.
- `OrderLine` - product/customer/price snapshot per requested item.
- `OrderTransition` - auditable state changes.
- `OrderApproval` - who approved and why.
- `OrderCancellation` - cancellation reason and actor.

## Does not own

- Product definitions or live prices. Those belong to [`catalog`](catalog.md).
- Customer master data or credit policy. Those belong to [`customers`](customers.md).
- Physical stock reservations. Those belong to [`inventory`](inventory.md).
- Picking execution. That belongs to [`warehouse`](warehouse.md).
- Delivery route execution. That belongs to [`logistics`](logistics.md).
- Invoices and receivables. Those belong to [`billing`](billing.md).

## State machine

See [`states/order.md`](../states/order.md). The shared helper `canTransition()` in [`packages/types/src/orders.ts`](../../packages/types/src/orders.ts) is the code-level source for legal transitions.

## Commands

Implemented:

- `CreateOrderCommand`.
- `ApproveOrderCommand`.
- `CancelOrderCommand`.

Later:

- `MoveOrderToPickingCommand`.
- `MarkOrderReadyForRouteCommand`.
- `ConfirmOrderDeliveredCommand`.
- `SettleOrderCommand`.

## Events emitted

Implemented:

- `ORDER_CREATED`.
- `ORDER_APPROVED`.
- `ORDER_CANCELLED`.

Future:

- `ORDER_PICKING_STARTED`.
- `ORDER_READY_FOR_ROUTE`.
- `ORDER_DELIVERED`.
- `ORDER_SETTLED`.

## Events consumed

Implemented:

- `INVENTORY_RESERVATION_FAILED` — `InventoryReservationFailedOrdersHandler` auto-cancels orders still in `APPROVED` via `CancelOrderCommand` (actor: tenant `system` user, reason `auto: inventory reservation failed`). Idempotent if the order was already cancelled.

Future:

- `DELIVERY_CONFIRMED` from Logistics to mark delivered.
- `PAYMENT_ALLOCATED` from Billing to mark settled.

## Allowed dependencies

- May snapshot Catalog and Customer data at order creation.
- May emit events that ask Inventory/Warehouse/Logistics/Billing to react.
- Must not call Inventory/Warehouse/Logistics/Billing services directly.

## Boundary rules

1. Orders owns the commercial workflow, not operational execution.
2. Order lines keep historical product/price/customer snapshots.
3. Approving an order emits a fact. Inventory decides whether/how to reserve.
4. Orders should be idempotent by `commandId` because offline clients may retry.
5. Every state transition is auditable and tenant-scoped.

## First real workflow

```txt
CreateOrder
↓
ORDER_CREATED event
↓
ApproveOrder
↓
ORDER_APPROVED event
↓
Inventory reserves stock (or emits INVENTORY_RESERVATION_FAILED)
↓
Orders auto-cancels on reservation failure (APPROVED → CANCELLED)
↓
Warehouse generates picking
```

## HTTP surface

```txt
GET /orders?limit=20&cursor=<orderId>
GET /orders/:id
POST /orders
POST /orders/:id/approve
POST /orders/:id/cancel
```

`GET /orders` returns tenant-scoped order summaries with cursor pagination (`nextCursor` is the last item id on the page).

`GET /orders/:id` returns the order with lines and transition history.

`POST /orders` creates a tenant-scoped `Order` in `DRAFT`, persists its lines, records the initial transition, and writes `ORDER_CREATED` to the outbox in the same transaction.

`POST /orders/:id/approve` transitions `DRAFT -> APPROVED`, records the transition, and writes `ORDER_APPROVED` to the outbox in the same transaction.

`POST /orders/:id/cancel` transitions `DRAFT -> CANCELLED` or `APPROVED -> CANCELLED`, records the transition, and writes `ORDER_CANCELLED` to the outbox in the same transaction.

```txt
Idempotency-Key: <command id>
X-Correlation-Id: <request/workflow id>
```

Both headers are optional today but part of the command metadata contract.

## Open questions

- Are prices locked at order creation or at approval?
- Is credit check synchronous in Orders or event-driven through Customers/Billing?
- Can one order be fulfilled from multiple branches, or must it split?
