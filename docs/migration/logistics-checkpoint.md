# CHECKPOINT LOGISTICS

Date: 2026-07-12  
Status: **closed** — Sales remains blocked until explicit approval.

## Auditoría Nest

See `docs/migration/logistics-nest-audit.md`. Matrix covers candidates, routes, stops, assign, dispatch, proof, confirm, fail, liquidate, MinIO, COD, and Nest gaps (`SKIPPED` / route `CANCELLED` unused).

## Estados Route / Stop / Candidate

| Aggregate              | States                                 | Writers                                                 |
| ---------------------- | -------------------------------------- | ------------------------------------------------------- |
| DeliveryRoute          | `PLANNED` → `DISPATCHED` → `COMPLETED` | HTTP create/assign/dispatch; auto-complete on last stop |
| DeliveryRoute          | `CANCELLED`                            | **Reserved / no public Cancel** (architecture test)     |
| DeliveryRouteStop      | `PLANNED` → `DELIVERED` \| `FAILED`    | confirm / report-failed                                 |
| DeliveryRouteStop      | `SKIPPED`                              | **Reserved / no public Skip** (architecture test)       |
| DeliveryRouteCandidate | `READY` ↔ `ASSIGNED`; `CANCELLED`      | Strategy A on `ORDER_READY`; assign; `ORDER_CANCELLED`  |

Concurrency: PostgreSQL `xmin` on Route, Candidate, Stop. Unique stop `(tenantId, deliveryRouteId, orderId)`.

## Candidate Strategy A (Nest parity)

`ORDER_READY_FOR_DELIVERY_ROUTE`:

1. Idempotent if `CreatedFromEventId` matches event id
2. Missing → create `READY`
3. `READY` → update `branchId` only
4. `ASSIGNED` → Reopen (`READY`, clear `DeliveryRouteId`, new event id)
5. `CANCELLED` → skip (do not reopen)

## ORDER_CANCELLED hardening (.NET vs Nest)

Nest: cancel candidate only.  
.NET: cancel candidate **and** remove `PLANNED` stops from `PLANNED` routes for that `orderId`. Historical stops on `DISPATCHED`/`COMPLETED` routes stay.

## Triggers

| Event                             | Producer  | Consumer        | Effect                                                         |
| --------------------------------- | --------- | --------------- | -------------------------------------------------------------- |
| `ORDER_READY_FOR_DELIVERY_ROUTE`  | Orders    | Logistics       | Strategy A candidate upsert/reopen                             |
| `ORDER_CANCELLED`                 | Orders    | Logistics       | Candidate `CANCELLED` + remove planned stops on planned routes |
| `DELIVERY_ROUTE_*` / `DELIVERY_*` | Logistics | none for Orders | Sync via `IOrderFulfillmentApi`                                |

Architecture test: Orders must not register processors for delivery events.

## Integración Orders

Logistics references **only** `Orders.Contracts` (+ `Platform.Features.Contracts` for `ITenantFeatureService`):

- `MarkOutForDeliveryAsync` / batch
- `MarkDeliveredAsync`
- `MarkDeliveryAttemptFailedAsync`
- `SettleCodOrdersAsync`
- `GetCashCollectionFactsAsync` (tenant-filtered, requested ids only; no `SaveChanges` in fulfillment service)

Requeue / cancel after failed delivery remain Orders HTTP; they emit events Logistics already consumes.

## Proof / object storage

- Provider is explicit: `Logistics:Storage:Provider` = `Local` \| `MinIO` (never inferred from empty credentials)
- Development/Testing: `Local` allowed; `MinIO` allowed when creds present
- Production/Staging: must be `MinIO` with Endpoint, Bucket, AccessKey, SecretKey — fail startup otherwise (`IValidateOptions` + `ValidateOnStart`)
- Presign: server decides bucket, key, TTL (clamped Min/Max), MIME, max size; client sends kind, contentType, sizeBytes
- Optional `Idempotency-Key` on proof-uploads → `delivery_proof_upload_intents`; same payload replays key; different payload → `IDEMPOTENCY_KEY_REUSED`
- Confirm: `ExistsAsync` / HeadObject **outside** PG TX (endpoint), then short TX (stop + proof + Orders + outbox)
- Keys: `tenants/{tenantId}/delivery-proofs/{stopId}/{photo\|signature}-{uuid}.{ext}` — other-stop keys rejected
- Filtered unique indexes on `PhotoObjectKey` / `SignatureObjectKey` where not null
- Object lifecycle: Presigned → UploadedExternally → Verified → AttachedToDelivery
- `LocalObjectStorage`: Exists=true only for issued (presigned) keys
- MinIO Testcontainers: `MinioProofStorageTests`

## Liquidación COD

Gates (in order):

1. `Features:LiquidationKillSwitch` (default **true** = allow) — operational only; false → `LIQUIDATION_DISABLED`
2. `TenantFeature` key `LIQUIDATION` — false → `FEATURE_DISABLED`
3. Role `ADMIN` or `SUPER_ADMIN` — else `LIQUIDATION_FORBIDDEN`

Commercial entitlement is DB-backed (`tenant_features`, ADR-0009). Seed upserts all `FeatureKeys` enabled=false for demo tenant. Liquidation lines snapshot `PaymentMethod` + `Included=true` at liquidate time (no historical re-query of Orders).

## Idempotencia

| Write          | Idempotency-Key                                            |
| -------------- | ---------------------------------------------------------- |
| create route   | yes (CreationOperationKey)                                 |
| assign         | yes                                                        |
| dispatch       | yes; different key when already DISPATCHED → success no-op |
| confirm / fail | yes                                                        |
| liquidate      | yes (conflict if already liquidated)                       |
| proof-uploads  | optional; intent table when present                        |

Same key + different payload on proof-uploads → `IDEMPOTENCY_KEY_REUSED`.

## Concurrencia (PostgreSQL)

Covered in `LogisticsConcurrencyTests`: dual dispatch, concurrent assign, same order two routes, assign vs cancel, confirm vs fail / dual confirm / dual fail / last-stop terminal. Cross-module rollback when Orders fails after MarkDelivered staging (`LogisticsRollbackTests`).

## Migración EF/SQL

- `20260712052515_Logistics_DeliveryRoutes` — candidates, routes, stops, proofs, liquidations
- `20260712062139_Logistics_CloseAdjustments` — `tenant_features`, proof upload intents, liquidation `payment_method`/`included`, stop unique `(tenant, route, order)`, filtered UQ on proof object keys

## Feature LIQUIDATION

Config: `Features:LiquidationKillSwitch` (ops). Entitlement: `TenantFeature` / `ITenantFeatureService`.  
Development seed enables `LIQUIDATION` for the demo tenant; Testing leaves it false until tests call `SetEnabledAsync`.

## Local storage (proof uploads)

When `Logistics:Storage:Provider=Local`, `Endpoint` is the API base (`http://localhost:5102`). Presign returns `PUT /internal/dev-object-storage/{key}` (Dev/Testing only) so the browser upload succeeds. MinIO: set Provider=MinIO, Endpoint to the S3 host, and bucket CORS for `localhost:3000`.

## OpenAPI / SDK

Regenerated with Logistics endpoints.

## Tests

| Project                     | Count                                             |
| --------------------------- | ------------------------------------------------- |
| Architecture                | 27                                                |
| Unit                        | 48                                                |
| Integration                 | 117 (includes MinIO Testcontainers + concurrency) |
| **Total**                   | **192**                                           |
| Failed / skipped / warnings | **0 / 0 / 0**                                     |

## NuGet audit / restore / build / test

```text
dotnet restore → OK
dotnet build -c Release → 0 errors / 0 warnings
dotnet test  -c Release → 192/192
dotnet list package --vulnerable --include-transitive → limpio (Api, Logistics, IntegrationTests)
dotnet ef migrations has-pending-model-changes → No changes
OpenAPI → artifacts/openapi/binexus-v1.json
SDK → packages/sdk/src/generated/schema.d.ts
```

## Nest divergences documented

1. `ORDER_CANCELLED` also strips planned stops from planned routes (.NET hardening).
2. Dual dispatch with different keys when already `DISPATCHED` → success no-op (documented).
3. Route `CANCELLED` / stop `SKIPPED` remain reserved without HTTP writers.

## Riesgos residuales

1. MinIO TOCTOU between Exists and commit — mitigated by tenant-scoped keys, stop state in TX, UQ object keys.
2. Historical FAILED stops retained after Orders requeue (Nest parity; Strategy A).
3. Sales not started.
