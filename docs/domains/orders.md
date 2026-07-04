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

Implemented (warehouse flow):

- `MoveOrderToPickingCommand` — `APPROVED -> PICKING`, emits `ORDER_PICKING_STARTED`.
- `MarkOrderReadyForDeliveryRouteCommand` — `PICKING -> READY_FOR_DELIVERY_ROUTE`.
- `MarkOrderOutForDeliveryCommand` — `READY_FOR_DELIVERY_ROUTE -> OUT_FOR_DELIVERY` (no new domain event in dispatch slice).
- `MarkOrderDeliveredCommand` — `OUT_FOR_DELIVERY -> DELIVERED`, emits `ORDER_DELIVERED`.
- `MarkOrderDeliveryAttemptFailedCommand` — `OUT_FOR_DELIVERY -> DELIVERY_ATTEMPT_FAILED` (no new domain event; triggered by `DELIVERY_FAILED`).
- `RequeueFailedDeliveryOrderCommand` — `DELIVERY_ATTEMPT_FAILED -> READY_FOR_DELIVERY_ROUTE`, emits `ORDER_READY_FOR_DELIVERY_ROUTE`.
- `SettleOrderCommand` — `DELIVERED -> SETTLED`, emits `ORDER_SETTLED` (COD via route liquidation; prepaid auto on delivery per ADR-0012 D1).

Later:

## Events emitted

Implemented:

- `ORDER_CREATED`.
- `ORDER_APPROVED`.
- `ORDER_CANCELLED`.

Implemented:

- `ORDER_PICKING_STARTED` — emitted when order moves to picking after reservation.
- `ORDER_READY_FOR_DELIVERY_ROUTE` — emitted when order moves to `READY_FOR_DELIVERY_ROUTE` after picking.
- `ORDER_DELIVERED` — emitted when order moves to `DELIVERED` after delivery confirmation.
- `ORDER_SETTLED` — operational closure (see [`states/order.md`](../states/order.md#settled-semantics-adr-0012)).

Future:

## Events consumed

Implemented:

- `INVENTORY_RESERVATION_FAILED` — `InventoryReservationFailedOrdersHandler` auto-cancels orders still in `APPROVED` via `CancelOrderCommand` (actor: tenant `system` user, reason `auto: inventory reservation failed`). Idempotent if the order was already cancelled.

Implemented:

- `INVENTORY_RESERVED` — auto `MoveOrderToPickingCommand` via system user when order is `APPROVED`.
- `PICKING_COMPLETED` — auto `MarkOrderReadyForDeliveryRouteCommand` when order is `PICKING`.
- `DELIVERY_ROUTE_DISPATCHED` — auto `MarkOrderOutForDeliveryCommand` via system user when order is `READY_FOR_DELIVERY_ROUTE` (skips already `OUT_FOR_DELIVERY` or cancelled orders).
- `DELIVERY_CONFIRMED` — auto `MarkOrderDeliveredCommand` via system user when order is `OUT_FOR_DELIVERY`.
- `DELIVERY_FAILED` — auto `MarkOrderDeliveryAttemptFailedCommand` via system user when order is `OUT_FOR_DELIVERY`.
- `DELIVERY_ROUTE_LIQUIDATED` — auto `SettleOrderCommand` for each COD order in `DELIVERED`.

Future:

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
Warehouse generates picking (automatic after `INVENTORY_RESERVED`)
↓
PICKING_COMPLETED → READY_FOR_DELIVERY_ROUTE
↓
ORDER_READY_FOR_DELIVERY_ROUTE → Logistics candidate projection
↓
Dispatch delivery route (Logistics)
↓
DELIVERY_ROUTE_DISPATCHED → OUT_FOR_DELIVERY
↓
Confirm delivery stop (Logistics)
↓
DELIVERY_CONFIRMED → DELIVERED (+ ORDER_DELIVERED)
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

`POST /orders` creates a tenant-scoped `Order` in `DRAFT`, persists its lines, records the initial transition, and writes `ORDER_CREATED` to the outbox in the same transaction. **`paymentMethod` is required** (`CASH` | `CARD` | `TRANSFER` | `CREDIT`).

`POST /orders/:id/approve` transitions `DRAFT -> APPROVED`, records the transition, and writes `ORDER_APPROVED` to the outbox in the same transaction.

`POST /orders/:id/cancel` transitions `DRAFT -> CANCELLED`, `APPROVED -> CANCELLED`, or `DELIVERY_ATTEMPT_FAILED -> CANCELLED`, records the transition, and writes `ORDER_CANCELLED` to the outbox in the same transaction.

`POST /orders/:id/requeue-for-delivery` transitions `DELIVERY_ATTEMPT_FAILED -> READY_FOR_DELIVERY_ROUTE`, records the transition, and writes `ORDER_READY_FOR_DELIVERY_ROUTE` to the outbox (Logistics resets `DeliveryRouteCandidate` to `READY`).

```txt
Idempotency-Key: <command id>
X-Correlation-Id: <request/workflow id>
```

Both headers are optional today but part of the command metadata contract.

## Open questions

- Are prices locked at order creation or at approval?
- Is credit check synchronous in Orders or event-driven through Customers/Billing?
- Can one order be fulfilled from multiple branches, or must it split?
