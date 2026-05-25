# Logistics domain

Status: **active** (planning slice). Bounded context: `logistics`.

Logistics owns delivery route planning, dispatch handoff, delivery confirmation, failed delivery handling, and route liquidation. Planning starts after Orders, Inventory, and Warehouse produce route-ready work.

## Naming convention

Models use explicit compound names to avoid collision with framework concepts (HTTP routes, dispatcher services, etc.) and to read clearly cross-context in `packages/types` and the SDK. See [`docs/architecture/naming-conventions.md`](../architecture/naming-conventions.md).

## Owns

- `DeliveryRoute` - planned delivery route (aggregate root).
- `DeliveryRouteStop` - customer/order stop in a delivery route.
- `DeliveryRouteCandidate` - projection of orders ready for route assignment.
- `DispatchHandoff` - handoff from branch/warehouse to driver (planned).
- `DeliveryProof` - confirmation, signature/photo/GPS metadata (planned).
- `DeliveryRouteLiquidation` - cash/returns/reconciliation at route close (planned).

## Does not own

- Order commercial lifecycle. That belongs to [`orders`](orders.md).
- Warehouse staging/picking. That belongs to [`warehouse`](warehouse.md).
- Payment allocation and receivables. Those belong to [`billing`](billing.md).

## Commands

Implemented (planning slice):

- `CreateDeliveryRouteCommand` - creates `DeliveryRoute(PLANNED)`.
- `AssignOrderToDeliveryRouteCommand` - assigns `READY` candidates as stops on a planned route.

Planned:

- `DispatchDeliveryRouteCommand`.
- `ConfirmDeliveryCommand`.
- `ReportFailedDeliveryCommand`.
- `StartDeliveryRouteLiquidationCommand`.
- `CloseDeliveryRouteLiquidationCommand`.

## Events emitted

Implemented:

- `DELIVERY_ROUTE_CREATED`.
- `DELIVERY_ROUTE_ASSIGNED`.

Planned:

- `DELIVERY_ROUTE_DISPATCHED`.
- `DELIVERY_CONFIRMED`.
- `DELIVERY_FAILED`.
- `DELIVERY_ROUTE_LIQUIDATED`.

## Events consumed

Implemented:

- `ORDER_READY_FOR_DELIVERY_ROUTE` from Orders - upserts `DeliveryRouteCandidate(READY)`.

Planned:

- `ORDER_CANCELLED` from Orders - mark candidate cancelled or exception a stop.
- `PAYMENT_REGISTERED` from Sales/Billing - reconcile delivery route cash.

## Allowed dependencies

- May use branch/user context from Identity for driver authorization.
- May emit delivery facts that Orders/Billing consume.
- Must not create invoices or settle receivables directly.
- Must not query Orders tables directly; route-ready work arrives via `ORDER_READY_FOR_DELIVERY_ROUTE`.

## Boundary rules

1. Logistics proves delivery; Billing decides financial settlement.
2. Delivery confirmation is an event with proof metadata, not a direct order update.
3. Delivery route liquidation is operational cash reconciliation, not accounting final truth.
4. Offline mobile/driver flows must be idempotent by command ID.

## HTTP surface

```txt
GET /logistics/delivery-route-candidates?status=READY&branchId=&limit=50&cursor=
GET /logistics/delivery-routes?status=PLANNED&branchId=&limit=50&cursor=
POST /logistics/delivery-routes
POST /logistics/delivery-routes/:id/assign-orders
```

## Web UI

- `/logistics` - list ready candidates and planned routes; prompt-based create route and assign orders.

## Open questions

- Are delivery routes pre-planned by dispatcher or generated automatically from zones?
- Does delivery route liquidation live fully in Logistics or split with Billing once accounting is richer?
