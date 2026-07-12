# Logistics domain

Status: **active** (planning + dispatch + confirmation + proof uploads + failed delivery + route liquidation).

Logistics owns delivery route planning, dispatch handoff, delivery confirmation, failed delivery handling, and route liquidation. Planning starts after Orders, Inventory, and Warehouse produce route-ready work.

## Naming convention

Models use explicit compound names to avoid collision with framework concepts (HTTP routes, dispatcher services, etc.) and to read clearly cross-context in `packages/types` and the SDK. See [`docs/architecture/naming-conventions.md`](../architecture/naming-conventions.md).

## Owns

- `DeliveryRoute` - planned delivery route (aggregate root).
- `DeliveryRouteStop` - customer/order stop in a delivery route.
- `DeliveryRouteCandidate` - projection of orders ready for route assignment.
- `DispatchHandoff` - handoff from branch/warehouse to driver (planned).
- `DeliveryProof` - confirmation metadata (notes, recipient, MinIO object keys, GPS) tied one-to-one to a delivered stop.
- `DeliveryRouteLiquidation` - COD cash reconciliation at route close (implemented #4).

## Does not own

- Order commercial lifecycle. That belongs to [`orders`](orders.md).
- Warehouse staging/picking. That belongs to [`warehouse`](warehouse.md).
- Payment allocation and receivables. Those belong to [`billing`](billing.md).

## Commands

Implemented:

- `CreateDeliveryRouteCommand` - creates `DeliveryRoute(PLANNED)`.
- `AssignOrderToDeliveryRouteCommand` - assigns `READY` candidates as stops on a planned route.
- `DispatchDeliveryRouteCommand` - transitions `PLANNED -> DISPATCHED`, sets driver and dispatch metadata, emits `DELIVERY_ROUTE_DISPATCHED`.
- `ConfirmDeliveryCommand` - marks stop `PLANNED -> DELIVERED`, optionally persists `DeliveryProof`, auto-completes route when all stops are terminal (`DELIVERED | FAILED | SKIPPED`), emits `DELIVERY_CONFIRMED` (payload may include optional `proof`). Validates tenant-scoped proof object keys and verifies uploaded media exists in MinIO when object keys are provided.
- `CreateDeliveryProofUploadCommand` - issues short-lived, tenant-scoped MinIO presigned upload URLs for proof photos/signatures on a `PLANNED` stop of a `DISPATCHED` route.
- `ReportFailedDeliveryCommand` - marks stop `PLANNED -> FAILED` with failure metadata, auto-completes route when all stops are terminal, emits `DELIVERY_FAILED`.
- `LiquidateDeliveryRouteCommand` - single-shot COD cash reconciliation on `COMPLETED` routes (hybrid B3: route total; stop breakdown on discrepancy). Gated by `@RequireFeature(LIQUIDATION)`.

## Events emitted

Implemented:

- `DELIVERY_ROUTE_CREATED`.
- `DELIVERY_ROUTE_ASSIGNED`.
- `DELIVERY_ROUTE_DISPATCHED` - informational audit/future projection fact. Logistics calls Orders synchronously through `Orders.Contracts` before commit.
- `DELIVERY_CONFIRMED` - informational audit/future projection fact with optional proof metadata. Logistics calls Orders synchronously through `Orders.Contracts` before commit.
- `DELIVERY_FAILED` - informational audit/future projection fact. Logistics calls Orders synchronously through `Orders.Contracts` before commit.
- `DELIVERY_ROUTE_LIQUIDATED` - informational audit/future projection fact. Logistics settles COD orders synchronously through `Orders.Contracts` before commit.

Planned:

## Events consumed

Implemented:

- `ORDER_READY_FOR_DELIVERY_ROUTE` from Orders - upserts or re-queues `DeliveryRouteCandidate(READY)` (including `ASSIGNED -> READY` on requeue).
- `ORDER_CANCELLED` from Orders - marks `DeliveryRouteCandidate` cancelled when present.

Planned:

- `PAYMENT_REGISTERED` from Sales/Billing - reconcile delivery route cash.

## Allowed dependencies

- May use branch/user context from Identity for driver authorization.
- May emit delivery facts that Orders/Billing consume.
- Must not create invoices or settle receivables directly.
- Must not query Orders tables directly; route-ready work arrives via `ORDER_READY_FOR_DELIVERY_ROUTE`.

## Boundary rules

1. Logistics proves delivery; Billing decides financial settlement.
2. Delivery confirmation emits a proof fact and updates Orders synchronously through `Orders.Contracts`.
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
POST /logistics/delivery-route-stops/:id/report-failed-delivery
  Body: `{ reason: "NO_RECIPIENT" | "WRONG_ADDRESS" | "REFUSED" | "DAMAGED" | "OTHER", notes?: string }`
  Returns: `{ deliveryRouteStopId, orderId, status: "FAILED", failedAt, failureReason, routeStatus, routeStopCounts }`
POST /logistics/delivery-routes/:id/liquidate
  Body: `{ declaredCents: number, notes?, discrepancyReason?, lines?: [{ deliveryRouteStopId, declaredCents }] }`
  Requires: feature `LIQUIDATION`, roles `ADMIN` | `SUPER_ADMIN`
```

Object keys issued by `proof-uploads` follow:

```txt
tenants/<tenantId>/delivery-proofs/<stopId>/<photo|signature>-<uuid>.<ext>
```

## Web UI

- `/logistics` - … completed routes with **Liquidate route** (COD arqueo) when `LIQUIDATION` feature enabled.

## Out of scope (follow-up slices)

- Driver mobile/offline capture.
- Partial liquidations / reopen closed liquidations.
- Presigned GET URLs for proof media in the UI (bucket remains private; use short-lived presigned GET when needed).
- Virus scanning or long-term retention policies.
- Orphan object cleanup in MinIO.

## Open questions

- Are delivery routes pre-planned by dispatcher or generated automatically from zones?
- Does delivery route liquidation live fully in Logistics or split with Billing once accounting is richer?
