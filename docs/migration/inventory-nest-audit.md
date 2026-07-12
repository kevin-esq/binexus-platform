> **KEEP_HISTORICAL** — Nest deleted in Gate 7 (ADR-0015). Audit matrix kept for migration history.

# Inventory — auditoría Nest → .NET (pre-implementación)

**Fecha:** 2026-07-11  
**Fuentes:** `apps/backend/src/contexts/inventory/**`, sales `CreateSaleHandler`, orders inventory event handlers, Prisma schema, `packages/types/src/inventory.ts`

## Matriz

| Comportamiento   | Endpoint/handler Nest             | Tablas                                   | Invariantes                                                | Evento                      | Test existente        | Destino .NET                        |
| ---------------- | --------------------------------- | ---------------------------------------- | ---------------------------------------------------------- | --------------------------- | --------------------- | ----------------------------------- |
| List stock       | `GET /inventory/stock`            | `StockItem`                              | `available = onHand - reserved`; tenant scoped             | —                           | inventory-read specs  | Query HTTP exacta                   |
| Adjust           | `POST /inventory/stock/adjust`    | StockItem, StockMovement ADJUSTMENT      | delta≠0; nextOnHand≥0; nextOnHand≥reserved                 | —                           | adjust-stock specs    | Command + ledger                    |
| Create transfer  | `POST /inventory/stock/transfers` | StockItem, StockTransfer                 | branches distintas; available≥qty; **reserved+=**; PENDING | —                           | create-stock-transfer | Hold vía reserved                   |
| List transfers   | `GET /inventory/stock/transfers`  | StockTransfer                            | status filter opcional                                     | —                           | tipos                 | Query HTTP                          |
| Receive          | `POST .../receive`                | StockItems, Transfer, Movements          | solo PENDING; src onHand− reserved−; dst onHand+           | movements TRANSFER_OUT/IN   | receive specs         | Misma semántica                     |
| Cancel           | `POST .../cancel`                 | StockItem, Transfer                      | solo PENDING; reserved−=                                   | —                           | cancel specs          | Misma                               |
| Reserve (Orders) | `@OnEvent(ORDER_APPROVED)`        | Reservation, StockItem, Movement RESERVE | all-or-nothing; reserved+=                                 | INVENTORY_RESERVED / FAILED | reservation specs     | **Sync** `IInventoryReservationApi` |
| Release          | `@OnEvent(ORDER_CANCELLED)`       | Reservation RELEASED, Movement RELEASE   | ACTIVE only                                                | INVENTORY_RELEASED          | same                  | Sync en cancel futuro               |
| Sale decrement   | `CreateSaleHandler` (sales)       | StockItem, Movement SALE                 | available≥qty; onHand−=; no toca reserved                  | SALE_CREATED (sales)        | create-sale specs     | `IInventorySaleApi`                 |

## Hechos críticos

1. **Int** onHand/reserved; available computado, nunca persistido.
2. **No** onHand negativo en adjust/sale/receive.
3. Transfer create: solo reserved; onHand se mueve **al receive**.
4. productId opaco (sin Catalog).
5. Idempotency-Key Nest = correlation only (sin store) — .NET debe añadir constraints reales donde aplique.
6. Feature `INVENTORY` no bloquea endpoints hoy — preservar paridad.
7. Shared reserved pool: órdenes + transfers PENDING.
