# Logistics domain

Status: **active** (planning + dispatch + confirmation + proof base). Bounded context: `logistics`.

Logistics owns delivery route planning, dispatch handoff, delivery confirmation, failed delivery handling, and route liquidation. Planning starts after Orders, Inventory, and Warehouse produce route-ready work.

## Naming convention

Models use explicit compound names to avoid collision with framework concepts (HTTP routes, dispatcher services, etc.) and to read clearly cross-context in `packages/types` and the SDK. See [`docs/architecture/naming-conventions.md`](../architecture/naming-conventions.md).

## Owns

- `DeliveryRoute` - planned delivery route (aggregate root).
- `DeliveryRouteStop` - customer/order stop in a delivery route.
- `DeliveryRouteCandidate` - projection of orders ready for route assignment.
- `DispatchHandoff` - handoff from branch/warehouse to driver (planned).
- `DeliveryProof` - confirmation metadata (notes, recipient, MinIO object keys, GPS) tied one-to-one to a delivered stop.
- `DeliveryRouteLiquidation` - cash/returns/reconciliation at route close (planned).

## Does not own

- Order commercial lifecycle. That belongs to [`orders`](orders.md).
- Warehouse staging/picking. That belongs to [`warehouse`](warehouse.md).
- Payment allocation and receivables. Those belong to [`billing`](billing.md).

## Commands

Implemented:

- `CreateDeliveryRouteCommand` - creates `DeliveryRoute(PLANNED)`.
- `AssignOrderToDeliveryRouteCommand` - assigns `READY` candidates as stops on a planned route.
- `DispatchDeliveryRouteCommand` - transitions `PLANNED -> DISPATCHED`, sets driver and dispatch metadata, emits `DELIVERY_ROUTE_DISPATCHED`.
- `ConfirmDeliveryCommand` - marks stop `PLANNED -> DELIVERED`, optionally persists `DeliveryProof`, auto-completes route when all stops delivered, emits `DELIVERY_CONFIRMED` (payload may include optional `proof`).

Planned:

- `CreateDeliveryProofUploadCommand` - issues short-lived, tenant-scoped MinIO presigned upload URLs for proof photos/signatures.
- `ReportFailedDeliveryCommand`.
- `StartDeliveryRouteLiquidationCommand`.
- `CloseDeliveryRouteLiquidationCommand`.

## Events emitted

Implemented:

- `DELIVERY_ROUTE_CREATED`.
- `DELIVERY_ROUTE_ASSIGNED`.
- `DELIVERY_ROUTE_DISPATCHED` - consumed by Orders to mark assigned orders `OUT_FOR_DELIVERY`.
- `DELIVERY_CONFIRMED` - consumed by Orders to mark order `DELIVERED`; optional `proof` object (recipient, notes, photo/signature object keys, GPS).

Planned:

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
POST /logistics/delivery-routes/:id/dispatch
GET /logistics/delivery-routes/:id/stops
POST /logistics/delivery-route-stops/:id/proof-uploads
  Body: `{ kind: "PHOTO" | "SIGNATURE", contentType: string, sizeBytes: number }`
  Returns: `{ objectKey, uploadUrl, expiresAt }`
POST /logistics/delivery-route-stops/:id/confirm-delivery
  Body (optional): `{ proof?: { recipientName?, notes?, photoObjectKey?, signatureObjectKey?, latitude?, longitude? } }`
```

## Web UI

- `/logistics` - list ready candidates, planned routes (assign + dispatch), dispatched routes with expandable stops, **Confirm delivery** (optional proof prompts), proof summary column on stops, and completed routes with `completedAt`.

## Next slice — Presigned Proof Upload Base

Goal: replace manual MinIO object-key prompts with a backend-issued upload flow for proof media.

Scope:

- Add `CreateDeliveryProofUploadCommand`.
- Add `POST /logistics/delivery-route-stops/:id/proof-uploads`.
- Return a short-lived presigned URL plus the object key that `ConfirmDeliveryCommand` will later persist in `DeliveryProof`.
- Enforce tenant-scoped object keys, e.g. `tenants/<tenantId>/delivery-proofs/<stopId>/<kind>-<uuid>`.
- Validate allowed proof media kinds (`PHOTO`, `SIGNATURE`), content type, and max size before issuing the URL.
- Update `/logistics` so proof photo/signature fields upload files first, then pass object keys into confirmation.

Out of scope:

- Driver mobile/offline capture.
- Failed delivery (`DELIVERY_FAILED`).
- Public read/download URLs for proof media.
- Virus scanning or long-term retention policies.

## Open questions

- Are delivery routes pre-planned by dispatcher or generated automatically from zones?
- Does delivery route liquidation live fully in Logistics or split with Billing once accounting is richer?
