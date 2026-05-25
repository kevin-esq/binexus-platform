# Logistics domain

Status: **planned** (Phases 4-6). Bounded context: `logistics`.

Logistics owns delivery route planning, dispatch handoff, delivery confirmation, failed delivery handling, and route liquidation. It starts after Orders, Inventory, and Warehouse can produce route-ready work.

## Naming convention

Models use explicit compound names to avoid collision with framework concepts (HTTP routes, dispatcher services, etc.) and to read clearly cross-context in `packages/types` and the SDK. See [`docs/architecture/naming-conventions.md`](../architecture/naming-conventions.md).

## Owns

- `DeliveryRoute` - planned delivery route (aggregate root).
- `DeliveryRouteStop` - customer/order stop in a delivery route.
- `DispatchHandoff` - handoff from branch/warehouse to driver.
- `DeliveryProof` - confirmation, signature/photo/GPS metadata.
- `DeliveryRouteLiquidation` - cash/returns/reconciliation at route close.

## Does not own

- Order commercial lifecycle. That belongs to [`orders`](orders.md).
- Warehouse staging/picking. That belongs to [`warehouse`](warehouse.md).
- Payment allocation and receivables. Those belong to [`billing`](billing.md).

## Commands

Planned:

- `CreateDeliveryRouteCommand`.
- `AssignOrderToDeliveryRouteCommand`.
- `DispatchDeliveryRouteCommand`.
- `ConfirmDeliveryCommand`.
- `ReportFailedDeliveryCommand`.
- `StartDeliveryRouteLiquidationCommand`.
- `CloseDeliveryRouteLiquidationCommand`.

## Events emitted

Planned:

- `DELIVERY_ROUTE_CREATED`.
- `DELIVERY_ROUTE_ASSIGNED`.
- `DELIVERY_ROUTE_DISPATCHED`.
- `DELIVERY_CONFIRMED`.
- `DELIVERY_FAILED`.
- `DELIVERY_ROUTE_LIQUIDATED`.

## Events consumed

- `ORDER_STAGED` from Warehouse - eligible for delivery route assignment.
- `ORDER_CANCELLED` from Orders - remove or exception a stop.
- `PAYMENT_REGISTERED` from Sales/Billing - reconcile delivery route cash.

## Allowed dependencies

- May use branch/user context from Identity for driver authorization.
- May emit delivery facts that Orders/Billing consume.
- Must not create invoices or settle receivables directly.

## Boundary rules

1. Logistics proves delivery; Billing decides financial settlement.
2. Delivery confirmation is an event with proof metadata, not a direct order update.
3. Delivery route liquidation is operational cash reconciliation, not accounting final truth.
4. Offline mobile/driver flows must be idempotent by command ID.

## Open questions

- Are delivery routes pre-planned by dispatcher or generated automatically from zones?
- Does delivery route liquidation live fully in Logistics or split with Billing once accounting is richer?
