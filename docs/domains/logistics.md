# Logistics domain

Status: **planned** (Phases 4-6). Bounded context: `logistics`.

Logistics owns route planning, dispatch, delivery confirmation, failed delivery handling, and route liquidation. It starts after Orders, Inventory, and Warehouse can produce route-ready work.

## Owns

- `Route` - planned delivery route.
- `RouteStop` - customer/order stop in a route.
- `Dispatch` - handoff from branch/warehouse to driver.
- `DeliveryProof` - confirmation, signature/photo/GPS metadata.
- `RouteLiquidation` - cash/returns/reconciliation at route close.

## Does not own

- Order commercial lifecycle. That belongs to [`orders`](orders.md).
- Warehouse staging/picking. That belongs to [`warehouse`](warehouse.md).
- Payment allocation and receivables. Those belong to [`billing`](billing.md).

## Commands

Planned:

- `CreateRouteCommand`.
- `AssignOrderToRouteCommand`.
- `DispatchRouteCommand`.
- `ConfirmDeliveryCommand`.
- `ReportFailedDeliveryCommand`.
- `StartRouteLiquidationCommand`.
- `CloseRouteLiquidationCommand`.

## Events emitted

Planned:

- `ROUTE_CREATED`.
- `ROUTE_ASSIGNED`.
- `ROUTE_DISPATCHED`.
- `DELIVERY_CONFIRMED`.
- `DELIVERY_FAILED`.
- `ROUTE_LIQUIDATED`.

## Events consumed

- `ORDER_STAGED` from Warehouse - eligible for route assignment.
- `ORDER_CANCELLED` from Orders - remove or exception a stop.
- `PAYMENT_REGISTERED` from Sales/Billing - reconcile route cash.

## Allowed dependencies

- May use branch/user context from Identity for driver authorization.
- May emit delivery facts that Orders/Billing consume.
- Must not create invoices or settle receivables directly.

## Boundary rules

1. Logistics proves delivery; Billing decides financial settlement.
2. Delivery confirmation is an event with proof metadata, not a direct order update.
3. Route liquidation is operational cash reconciliation, not accounting final truth.
4. Offline mobile/driver flows must be idempotent by command ID.

## Open questions

- Are routes pre-planned by dispatcher or generated automatically from zones?
- Does route liquidation live fully in Logistics or split with Billing once accounting is richer?
