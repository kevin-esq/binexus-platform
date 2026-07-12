> **KEEP_HISTORICAL** — Nest deleted in Gate 7 (ADR-0015). Audit matrix kept for migration history.

# Logistics Nest → .NET Pre-Implementation Audit

**Fecha:** 2026-07-12  
**Alcance:** `apps/backend/src/contexts/logistics/**`, handlers Orders de delivery, Prisma logistics, `@binexus/types`, `@binexus/events`, MinIO, feature `LIQUIDATION`.

## 1. Resumen Nest

Logistics es código real: rutas, candidatos, asignación, despacho, proof presigned, confirmación, failed delivery, auto-complete de ruta y liquidación COD.

Mismatches documentados:

- Docs dicen que Logistics no consulta Orders, pero `computeRouteCodExpected()` lee `order` para COD.
- `SKIPPED` / `DeliveryRoute.CANCELLED` existen en enums sin writers HTTP.
- Idempotencia HTTP vía `commandId` sin store genérico; mayormente state-based.
- Sin endpoint de cancelación de ruta.

## 2. Matriz

| Caso            | Estado inicial                  | Acción                           | Estado final                         | Orders             | Inventory            | Storage                   | Evento                        | HTTP                            | Test Nest              |
| --------------- | ------------------------------- | -------------------------------- | ------------------------------------ | ------------------ | -------------------- | ------------------------- | ----------------------------- | ------------------------------- | ---------------------- |
| List candidates | any                             | read                             | —                                    | —                  | —                    | —                         | —                             | GET …/delivery-route-candidates | read spec parcial      |
| List routes     | any                             | read                             | —                                    | —                  | —                    | —                         | —                             | GET …/delivery-routes           | —                      |
| List stops      | route exists                    | read                             | —                                    | —                  | —                    | —                         | —                             | GET …/delivery-routes/:id/stops | —                      |
| Create route    | —                               | create                           | PLANNED                              | —                  | —                    | —                         | DELIVERY_ROUTE_CREATED        | POST …/delivery-routes          | create spec            |
| Assign orders   | route PLANNED, candidates READY | stops + ASSIGNED                 | stops PLANNED                        | no change          | —                    | —                         | DELIVERY_ROUTE_ASSIGNED       | POST …/assign-orders            | assign spec            |
| Dispatch        | PLANNED + stops + driver        | DISPATCHED                       | stops PLANNED                        | → OUT_FOR_DELIVERY | —                    | —                         | DELIVERY_ROUTE_DISPATCHED     | POST …/dispatch                 | dispatch + Orders spec |
| Proof upload    | stop PLANNED, route DISPATCHED  | presign PUT                      | no DB                                | —                  | —                    | MinIO PUT                 | —                             | POST …/proof-uploads            | proof + MinIO specs    |
| Confirm         | stop PLANNED, route DISPATCHED  | DELIVERED; route maybe COMPLETED | → DELIVERED (+SETTLED card/transfer) | —                  | HeadObject before TX | DELIVERY_CONFIRMED        | POST …/confirm-delivery       | confirm + MinIO                 |
| Failed          | stop PLANNED                    | FAILED; route maybe COMPLETED    | → DELIVERY_ATTEMPT_FAILED            | —                  | —                    | DELIVERY_FAILED           | POST …/report-failed-delivery | failed + Orders                 |
| Liquidate       | route COMPLETED                 | liquidation once                 | Settle COD cashOrderIds              | —                  | —                    | DELIVERY_ROUTE_LIQUIDATED | POST …/liquidate              | liquidate + Orders              |

## 3. Estados

**Route:** PLANNED → DISPATCHED → COMPLETED (auto). CANCELLED unused.  
**Stop:** PLANNED → DELIVERED | FAILED. SKIPPED unused.  
**Candidate:** READY ↔ ASSIGNED; CANCELLED from ORDER_CANCELLED (no requeue).

## 4. Triggers

- In: `ORDER_READY_FOR_DELIVERY_ROUTE`, `ORDER_CANCELLED`
- Out: CREATED, ASSIGNED, DISPATCHED, CONFIRMED, FAILED, LIQUIDATED

## 5. Contratos Orders necesarios (.NET)

Extender `IOrderFulfillmentApi` / nuevo `IOrderDeliveryApi` en `Orders.Contracts`:

- MarkOutForDelivery (batch por route)
- MarkDelivered
- MarkDeliveryAttemptFailed
- RequeueForDelivery / CancelAfterFailedDelivery (HTTP Orders ya existe)
- SettleCodOrdersForLiquidatedRoute
- GetRouteCashCollectionFacts / GetOrderDeliveryReadiness (reads; no tables)

Resultados tipados (`Success|AlreadyApplied|NoLongerApplicable|NotFound|ConcurrencyConflict`), sin HTTP.

## 6. Failed / requeue / cancel

Failed stop → Orders DELIVERY_ATTEMPT_FAILED. Requeue Orders → READY + candidate READY. Cancel Orders → candidate CANCELLED + Inventory release. Riesgo: stops históricos no se eliminan.

## 7. MinIO

Key: `tenants/{tenantId}/delivery-proofs/{stopId}/{photo|signature}-{uuid}.{ext}`  
Allowlist MIME; size caps; HeadObject antes de TX corta; sin TX abierta durante I/O. Presign TTL ~900s.

## 8. Liquidación COD

Feature `LIQUIDATION` + ADMIN/SUPER_ADMIN. Solo COMPLETED, una vez. Expected = delivered stops con payment CASH. Discrepancy requiere reason + lines. Emite LIQUIDATED → Settle.

## 9. Idempotencia / concurrencia

State-based. Riesgos: assign concurrente (sequence), confirm vs fail, liquidate unique, proof orphans.

## 10. Gaps

COD lee Orders (romper en .NET con read contract). Sin DispatchHandoff. Sin route cancel HTTP. Sin proof GET/cleanup.

## 11. Principio TX .NET

Presign sin TX larga. Confirm: HeadObject fuera → TX corta (stop + Orders + outbox + commit).
