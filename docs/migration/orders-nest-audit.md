> **KEEP_HISTORICAL** — Nest deleted in Gate 7 (ADR-0015). Audit matrix kept for migration history.

# Orders — auditoría Nest → .NET (pre-implementación)

**Fecha:** 2026-07-12  
**Fuentes:** `apps/backend/src/contexts/orders/**`, Inventory/Warehouse/Logistics handlers, Prisma, `packages/types`, `packages/events`

## Matriz

| Caso                   | Estado inicial                         | Validaciones                                   | Estado final                                                | Inventory                             | Evento                                     | HTTP                         | Test Nest               | Destino .NET                                 |
| ---------------------- | -------------------------------------- | ---------------------------------------------- | ----------------------------------------------------------- | ------------------------------------- | ------------------------------------------ | ---------------------------- | ----------------------- | -------------------------------------------- |
| Create                 | —                                      | lines≥1, qty>0, cents≥0, currency ISO3, branch | DRAFT                                                       | —                                     | ORDER_CREATED                              | POST /orders                 | create spec             | Create command                               |
| Approve                | DRAFT                                  | canTransition                                  | Nest: APPROVED async; **.NET: APPROVED solo si reserve OK** | Nest async / **.NET sync TryReserve** | ORDER_APPROVED (+ INVENTORY_RESERVED sync) | POST …/approve               | approve                 | Sync reserve; 409 + DRAFT                    |
| Cancel                 | DRAFT/APPROVED/DELIVERY_ATTEMPT_FAILED | canTransition                                  | CANCELLED                                                   | release ACTIVE                        | ORDER_CANCELLED                            | POST …/cancel                | cancel                  | Sync release same TX                         |
| Auto-cancel stock fail | APPROVED                               | Nest only                                      | CANCELLED                                                   | FAILED path                           | consumes INVENTORY_RESERVATION_FAILED      | —                            | failed handler          | **Removed** under sync approve               |
| → Picking              | APPROVED                               | idempotent                                     | PICKING                                                     | expects ACTIVE                        | ORDER_PICKING_STARTED                      | event                        | move/reserved specs     | Keep event or direct post-approve (document) |
| Ready for route        | PICKING                                | via PICKING_COMPLETED                          | READY_FOR_DELIVERY_ROUTE                                    | —                                     | ORDER_READY_FOR_DELIVERY_ROUTE             | event                        | ready specs             | Keep                                         |
| Out for delivery       | READY…                                 | via DELIVERY_ROUTE_DISPATCHED                  | OUT_FOR_DELIVERY                                            | —                                     | _(Logistics event)_                        | event                        | OFD specs               | Keep                                         |
| Delivered              | OUT_FOR_DELIVERY                       | via DELIVERY_CONFIRMED                         | DELIVERED (or SETTLED CARD/TRANSFER)                        | **no SALE** today                     | ORDER_DELIVERED (+ SETTLED)                | event                        | delivered               | Keep; SALE gap documented                    |
| Attempt failed         | OUT_FOR_DELIVERY                       | via DELIVERY_FAILED                            | DELIVERY_ATTEMPT_FAILED                                     | —                                     | _(Logistics)_                              | event                        | failed specs            | Keep                                         |
| Requeue                | DELIVERY_ATTEMPT_FAILED                | canTransition                                  | READY_FOR_DELIVERY_ROUTE                                    | —                                     | ORDER_READY_FOR_DELIVERY_ROUTE             | POST …/requeue-for-delivery  | requeue                 | KEEP                                         |
| Settle COD             | DELIVERED + CASH                       | liquidate cashOrderIds                         | SETTLED                                                     | —                                     | ORDER_SETTLED                              | event                        | **missing settle spec** | Keep async                                   |
| List/Get               | any                                    | JWT                                            | —                                                           | —                                     | —                                          | GET /orders, GET /orders/:id | read                    | KEEP (cursor/limit only)                     |

## Estados

`DRAFT | APPROVED | PICKING | READY_FOR_DELIVERY_ROUTE | OUT_FOR_DELIVERY | DELIVERY_ATTEMPT_FAILED | DELIVERED | SETTLED | CANCELLED`

## Decisiones .NET ya aprobadas

1. Approve = misma TX + `IInventoryReservationApi`; fallo → DRAFT + 409; sin `INVENTORY_RESERVATION_FAILED` ficticio.
2. Cancel con reservas = release sync en misma TX.
3. Money = int cents; sin double.
4. customerId/productId opacos.
5. No Warehouse/Logistics reales en esta etapa.

## Orphans / gaps

- `ORDER_DELIVERED`, `ORDER_SETTLED`, `INVENTORY_RELEASED` sin consumers de negocio.
- Reserved stock never becomes SALE on delivery.
- Nest Idempotency-Key sin store real.
- AuditLog solo create/approve/cancel.
