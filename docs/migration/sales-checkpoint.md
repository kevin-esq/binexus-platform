# CHECKPOINT SALES

Date: 2026-07-12  
Status: **closed** — F5.2 POS walk-in only. F5.3 credit / F5.4 delivery-from-POS deferred.

## Auditoría Nest

See `docs/migration/sales-nest-audit.md`. Matrix covers open/current/get/create/close, `POS_RETAIL`, CASH|CARD|TRANSFER, inventory decrement, arqueo, and Nest gaps (no HTTP idempotency store; Ticket naming).

## Aggregates

| Aggregate              | States            | Notes                                                                                                                                                                         |
| ---------------------- | ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `SalesSession`         | `OPEN` → `CLOSED` | Partial unique OPEN `(tenantId, branchId, terminalId)`; `xmin` concurrency; close snapshot fields immutable after first close                                                 |
| `Sale` (Nest `Ticket`) | `COMPLETED`       | Separate AR; lines + payment captures; walk-in label; `saleId == ticketId` (HTTP JSON key still `ticket`); **no Ticket domain type**                                          |
| `PaymentCapture`       | child of Sale     | Declarative/manual capture for CARD/TRANSFER (not acquirer settlement); no update/delete endpoints; immutable after sale; composite FK `(tenantId, saleId, sessionId)` → Sale |

### Integrity (composite FKs)

- `sales_sessions` alternate key `(tenant_id, id)`
- `sales` alternate key `(tenant_id, id, session_id)`; FK `(tenant_id, session_id)` → session `(tenant_id, id)` **ON DELETE RESTRICT** (no cascade wipe of historical sales)
- `payment_captures` FK `(tenant_id, sale_id, session_id)` → sale alternate key **ON DELETE CASCADE** with sale
- Domain: Sale constructor rejects captures whose `SessionId`/`SaleId`/`TenantId` diverge

## HTTP (KEEP_EXACT)

| Method | Path                         |
| ------ | ---------------------------- |
| POST   | `/sales/sessions/open`       |
| GET    | `/sales/sessions/current`    |
| GET    | `/sales/sessions/{id}`       |
| POST   | `/sales/sessions/{id}/sales` |
| POST   | `/sales/sessions/{id}/close` |

All: `RequireAuthorization` + role `CASHIER\|ADMIN\|SUPER_ADMIN` + `ITenantFeatureService.IsEnabled(POS_RETAIL)` → `FEATURE_DISABLED` 403.  
JWT branch mismatch on create sale → `INVALID_BRANCH`.

## Inventory

Same TX via `IInventorySaleApi.DecrementForSaleAsync` (Inventory.Contracts). Inventory does not `SaveChanges`. Stock failure → sale rolls back. Idempotent by `SaleId`+`SaleLineId` movement keys. Inventory stages `STOCK_SOLD` (informational physical batch; distinct from commercial `SALE_CREATED`). Schema: `docs/events/schemas/stock-sold.v1.json`.

## Payments

`CASH\|CARD\|TRANSFER` only; CREDIT / invalid / zero / negative rejected. Sum must equal sale total (checked arithmetic). Split allowed. Arqueo = opening float + **this session's CASH captures only** (CARD/TRANSFER ignored). Client `declaredClosingCents` is not authoritative for expected cash.

## Events (outbox same TX)

| Event                  | When                                                                   |
| ---------------------- | ---------------------------------------------------------------------- |
| `SALES_SESSION_OPENED` | open                                                                   |
| `SALE_CREATED`         | create sale (`saleId` + `ticketId` same id; `sessionId`; `payments[]`) |
| `PAYMENT_REGISTERED`   | per capture (`saleId` + `sessionId`)                                   |
| `SALES_SESSION_CLOSED` | close                                                                  |
| `STOCK_SOLD`           | Inventory sale decrement (same TX)                                     |

Worker order is **per aggregate claim**, not globally guaranteed. Consumers must be order-tolerant and idempotent by event id / handler inbox.

## Idempotency

`Idempotency-Key` on open / create sale / close → tenant-scoped operation keys + unique indexes. Same key + different payload → `IDEMPOTENCY_KEY_REUSED`. Replay returns same `saleId`/`ticketId` without double inventory/events.

## Concurrency — `SELECT … FOR UPDATE` (not Touch)

**Problem:** `session.Touch` on create sale made the session row hot; concurrent legitimate sales fought on `xmin`.

**Decision:** Remove Touch from CreateSale. At the start of CreateSale and Close (inside the existing `ITransactionalCommand` transaction):

```sql
SELECT 1 FROM sales_sessions WHERE id = @sessionId AND tenant_id = @tenantId FOR UPDATE
```

Then load the session. This serializes sale/close per session so:

- two concurrent distinct sales both complete (sequentially)
- sale vs close: either the sale is included in arqueo **or** the sale is rejected after close — never a committed sale missing from close expected cash

No automatic retry of the full sale on lock wait; Postgres queues the second TX. `xmin` remains for dual-open / dual-close races and other session updates.

Covered in `SalesConcurrencyTests` (+ closing adjustment tests): last stock unit, dual open, concurrent distinct sales both succeed, sale-vs-close, close-then-sale, dual close.

## Feature POS_RETAIL

Contracts in `Binexus.Platform.Features.Contracts` (`FeatureKey.PosRetail`). Identity persists `tenant_features` and implements the port. **No appsettings kill switch** for POS. Tenant-scoped only. Seed creates all keys `enabled=false`. **Development** demo seed then enables `POS_RETAIL` (+ `LIQUIDATION`) for the seeded tenant; **Testing** keeps all false so tests call `SetEnabledAsync`.

## Migración EF/SQL

- `20260712064852_Sales_SessionsAndSales` — tables
- `20260712072341_Sales_ClosingAdjustments` — composite FKs / alternate keys

## OpenAPI / SDK

Regenerated with Sales endpoints (`artifacts/openapi/binexus-v1.json`, `packages/sdk/src/generated/schema.d.ts`).

## Tests

| Project                     | Count                                                          |
| --------------------------- | -------------------------------------------------------------- |
| Architecture                | 29                                                             |
| Unit                        | 57                                                             |
| Integration                 | 142 (Sales flow + concurrency + closing adjustments + catalog) |
| **Total**                   | **228**                                                        |
| Failed / skipped / warnings | **0 / 0 / 0**                                                  |

## NuGet audit / restore / build / test

```text
dotnet restore → OK
dotnet build -c Release → 0 errors / 0 warnings
dotnet test  -c Release → 228/228
dotnet list package --vulnerable --include-transitive → clean (Api, Sales)
dotnet ef migrations has-pending-model-changes → No changes
OpenAPI → artifacts/openapi/binexus-v1.json
SDK → packages/sdk/src/generated/schema.d.ts
```

## Nest divergences

1. Domain/table rename `Ticket` → `Sale`; HTTP create response still uses JSON key `ticket` for Nest/web parity (`saleId == ticketId`).
2. HTTP idempotency store on open/create/close (.NET); Nest only forwarded keys as causation.
3. Inventory via `IInventorySaleApi` + `STOCK_SOLD` outbox (.NET); Nest decremented inline without `STOCK_SOLD`.
4. Session serialization via `SELECT … FOR UPDATE` on create/close (.NET); Nest relied on Prisma TX without an explicit row lock / Touch pattern.
5. Unique OPEN terminal violation / concurrent open maps through dispatcher to conflict (domain `SALES_SESSION_ALREADY_OPEN` when detected pre-insert).
6. Entitlements port in `Platform.Features.Contracts` (.NET); Nest kept FeatureFlags in Identity/common.

## Deferred

Credit (F5.3), delivery-from-POS (F5.4), void/returns, Terminal catalog, `POS_RESTAURANT`. Gate 5 frontend switch: see [`frontend-switch-checkpoint.md`](./frontend-switch-checkpoint.md).
