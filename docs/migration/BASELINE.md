# Etapa 0 — Baseline de migración NestJS → .NET 10

**Estado:** CHECKPOINT — pendiente aprobación antes de Gate 2 (scaffold .NET)  
**Fecha:** 2026-07-10  
**Arquitectura:** [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)

---

## 1. Resumen ejecutivo

| Dimensión        | Baseline actual                                            |
| ---------------- | ---------------------------------------------------------- |
| Backend          | NestJS 11 + Fastify, `apps/backend/`                       |
| ORM              | Prisma 6, 26 modelos, 14 enums, 13 carpetas de migración   |
| IDs persistentes | `cuid()` (string)                                          |
| IDs de eventos   | `randomUUID()`                                             |
| Outbox           | Tabla `OutboxEvent`; **dispatcher no corre en producción** |
| Inbox            | **No existe**                                              |
| Tests            | 51 archivos `.spec.ts` (~170 casos), 1 integración MinIO   |
| HTTP             | ~37 rutas en 7 controllers                                 |
| Eventos          | 22 nombres en `packages/events`                            |
| SDK              | Manual en `packages/sdk`; sin `refresh()`                  |

**Decisiones base confirmadas:**

1. UUID v7 + base limpia EF Core
2. `backend/` en raíz del monorepo
3. OpenAPI → SDK TypeScript (sin SDK C# en este PR)
4. Sales → Inventory: API pública síncrona misma TX
5. Orders → Inventory: reserva síncrona en approve (Opción A)
6. Sin `IUnitOfWork` ceremonial

---

## 2. Matriz Prisma → EF Core

### 2.1 Convenciones globales

| Prisma                                    | EF Core / PostgreSQL                                             |
| ----------------------------------------- | ---------------------------------------------------------------- |
| `@id @default(cuid())`                    | `uuid` PK, generado **UUID v7** en aplicación                    |
| `DateTime`                                | `timestamptz`; columnas `*AtUtc`                                 |
| `Json`                                    | `jsonb`                                                          |
| `String` FK                               | `uuid` (mismo tipo que PK referenciada)                          |
| `@@index`                                 | Índices explícitos en `IEntityTypeConfiguration`                 |
| `@@unique`                                | Unique index                                                     |
| Índice parcial Prisma                     | `HasFilter` en EF                                                |
| Sin `RowVersion` hoy                      | Añadir `xmin` o `bytea RowVersion` en agregados con concurrencia |
| `tenantId` en todas las tablas de negocio | Global query filter + validación en commands                     |

### 2.2 Enums (14)

| Prisma enum                    | Módulo EF            | Notas                                 |
| ------------------------------ | -------------------- | ------------------------------------- |
| `Role`                         | Identity             |                                       |
| `PaymentMethod`                | Sales (+ Orders ref) | Valores: CASH, CARD, TRANSFER, CREDIT |
| `OrderState`                   | Orders               | Ver §4 semántica APPROVED             |
| `StockReservationStatus`       | Inventory            | ACTIVE, RELEASED, FAILED              |
| `StockTransferStatus`          | Inventory            |                                       |
| `StockMovementType`            | Inventory            |                                       |
| `SalesSessionStatus`           | Sales                | OPEN, CLOSED                          |
| `TicketStatus`                 | Sales                | COMPLETED (v1)                        |
| `PickingTaskStatus`            | Warehouse            |                                       |
| `DeliveryRouteStatus`          | Logistics            |                                       |
| `DeliveryRouteStopStatus`      | Logistics            |                                       |
| `DeliveryFailureReason`        | Logistics            |                                       |
| `DeliveryRouteCandidateStatus` | Logistics            |                                       |

**Renombres de dominio (opcional, no breaking HTTP):**

| Prisma       | .NET dominio     | Tabla EF                                |
| ------------ | ---------------- | --------------------------------------- |
| `Ticket`     | `Sale`           | `sales` o mantener `tickets` por compat |
| `TicketLine` | `SaleLine`       |                                         |
| `sessionId`  | `SalesSessionId` | FK explícita                            |

Decisión Etapa 1: **tablas SQL pueden conservar nombres Prisma** en migración inicial; entidades C# usan `Sale`. Documentar en ADR.

### 2.3 Modelos (26)

| #   | Prisma model                   | Módulo    | EF entity                      | PK      | Índices / constraints clave                     |
| --- | ------------------------------ | --------- | ------------------------------ | ------- | ----------------------------------------------- |
| 1   | `Tenant`                       | Identity  | `Tenant`                       | uuid v7 | `slug` UNIQUE                                   |
| 2   | `User`                         | Identity  | `User`                         | uuid v7 | `(tenantId, email)` UNIQUE                      |
| 3   | `Branch`                       | Identity  | `Branch`                       | uuid v7 | `(tenantId)`                                    |
| 4   | `RefreshToken`                 | Identity  | `RefreshToken`                 | uuid v7 | `tokenHash` UNIQUE                              |
| 5   | `TenantFeature`                | Platform  | `TenantFeature`                | uuid v7 | `(tenantId, key)` UNIQUE                        |
| 6   | `Order`                        | Orders    | `Order`                        | uuid v7 | `(tenantId, state)`, `(tenantId, branchId)`     |
| 7   | `OrderLine`                    | Orders    | `OrderLine`                    | uuid v7 | `(tenantId, orderId)`                           |
| 8   | `OrderTransition`              | Orders    | `OrderTransition`              | uuid v7 | append-only log                                 |
| 9   | `StockItem`                    | Inventory | `StockItem`                    | uuid v7 | `(tenantId, branchId, productId)` UNIQUE        |
| 10  | `StockReservation`             | Inventory | `StockReservation`             | uuid v7 | `(tenantId, orderId, orderLineId)` UNIQUE       |
| 11  | `StockMovement`                | Inventory | `StockMovement`                | uuid v7 | ledger append-only                              |
| 12  | `StockTransfer`                | Inventory | `StockTransfer`                | uuid v7 | lifecycle PENDING→RECEIVED/CANCELLED            |
| 13  | `PickingTask`                  | Warehouse | `PickingTask`                  | uuid v7 | `(tenantId, orderId)` UNIQUE                    |
| 14  | `PickingLine`                  | Warehouse | `PickingLine`                  | uuid v7 | `(tenantId, pickingTaskId, orderLineId)` UNIQUE |
| 15  | `DeliveryRoute`                | Logistics | `DeliveryRoute`                | uuid v7 | `(tenantId, status)`                            |
| 16  | `DeliveryRouteStop`            | Logistics | `DeliveryRouteStop`            | uuid v7 | `(tenantId, deliveryRouteId, orderId)` UNIQUE   |
| 17  | `DeliveryProof`                | Logistics | `DeliveryProof`                | uuid v7 | `deliveryRouteStopId` UNIQUE                    |
| 18  | `DeliveryRouteCandidate`       | Logistics | `DeliveryRouteCandidate`       | uuid v7 | `(tenantId, orderId)` UNIQUE                    |
| 19  | `DeliveryRouteLiquidation`     | Logistics | `DeliveryRouteLiquidation`     | uuid v7 | `deliveryRouteId` UNIQUE                        |
| 20  | `DeliveryRouteLiquidationLine` | Logistics | `DeliveryRouteLiquidationLine` | uuid v7 | `(tenantId, deliveryRouteStopId)` UNIQUE        |
| 21  | `SalesSession`                 | Sales     | `SalesSession`                 | uuid v7 | **partial UNIQUE** OPEN por terminal            |
| 22  | `Ticket`                       | Sales     | `Sale`                         | uuid v7 | `(tenantId, sessionId)` — **AR separado**       |
| 23  | `TicketLine`                   | Sales     | `SaleLine`                     | uuid v7 | hijo de Sale                                    |
| 24  | `PaymentCapture`               | Sales     | `PaymentCapture`               | uuid v7 | hijo de Sale; `sessionId` índice arqueo         |
| 25  | `OutboxEvent`                  | Platform  | `OutboxMessage`                | uuid v7 | ver §2.4                                        |
| 26  | `AuditLog`                     | Platform  | `AuditLogEntry`                | uuid v7 | `eventId` UNIQUE                                |

### 2.4 Extensiones de esquema (nuevas en .NET)

**OutboxEvent → OutboxMessage**

| Campo nuevo             | Tipo         | Propósito                                                        |
| ----------------------- | ------------ | ---------------------------------------------------------------- |
| `Status`                | enum         | Pending, Processing, Completed, FailedTransient, FailedPermanent |
| `ApplicableHandlerKeys` | jsonb        | **Snapshot inmutable** al primer claim del worker                |
| `LockedUntilUtc`        | timestamptz? | reclaim tras crash worker                                        |

**InboxMessage (nueva tabla)**

| Campo            | Tipo        | Notas                                      |
| ---------------- | ----------- | ------------------------------------------ |
| `Id`             | uuid v7     |                                            |
| `TenantId`       | uuid        |                                            |
| `EventId`        | uuid        | FK lógica a outbox                         |
| `HandlerKey`     | string      | ej. `orders.inventory-reserved`            |
| `ProcessedAtUtc` | timestamptz |                                            |
| `LastError`      | string?     | sin payload sensible                       |
|                  |             | **UNIQUE (TenantId, EventId, HandlerKey)** |

**SalesSession**

| Campo nuevo  | Tipo              |
| ------------ | ----------------- |
| `RowVersion` | concurrency token |

**Sale (Ticket)**

| Campo nuevo  | Tipo     |
| ------------ | -------- |
| `RowVersion` | opcional |

### 2.5 Relaciones a desacoplar en dominio

| Prisma navigation                | .NET                                                  |
| -------------------------------- | ----------------------------------------------------- |
| `SalesSession.tickets[]`         | **Eliminar** del AR; solo FK `SalesSessionId` en Sale |
| `SalesSession.paymentCaptures[]` | **Eliminar** del AR; query por `sessionId` al cerrar  |
| `Ticket.session`                 | FK por ID; no cargar sesión en create sale            |

### 2.6 Montos

| Prisma                                | EF                 | Compat                                    |
| ------------------------------------- | ------------------ | ----------------------------------------- |
| `totalCents`, `amountCents`, `*Cents` | `int` minor units  | 1:1                                       |
| `currency`                            | `char(3)` o string | ISO 4217                                  |
| `latitude/longitude` en DeliveryProof | `double` hoy       | Mantener en v1; evaluar decimal en futuro |

---

## 3. Inventario de contratos HTTP

**Base URL:** `http://localhost:3001` (sin global prefix)  
**Auth:** Bearer JWT en rutas protegidas  
**Validación:** `whitelist: true`, `forbidNonWhitelisted: true`

### 3.1 Clasificación

| Código            | Significado                                                       |
| ----------------- | ----------------------------------------------------------------- |
| `KEEP_EXACT`      | Misma ruta, método, body, status codes                            |
| `COMPATIBLE`      | Misma semántica; tipos ID string→uuid ok si cliente ya usa string |
| `BEHAVIOR_CHANGE` | Misma ruta; semántica distinta documentada                        |
| `NEW`             | No existía en Nest                                                |
| `DEPRECATE`       | Mantener temporalmente                                            |

### 3.2 Rutas

| Método | Ruta                                                         | Módulo    | Enforcement Nest      | Clasificación .NET  | Notas                                        |
| ------ | ------------------------------------------------------------ | --------- | --------------------- | ------------------- | -------------------------------------------- |
| GET    | `/health`                                                    | Platform  | —                     | KEEP_EXACT          |                                              |
| POST   | `/auth/login`                                                | Identity  | —                     | KEEP_EXACT          |                                              |
| POST   | `/auth/refresh`                                              | Identity  | —                     | KEEP_EXACT          | SDK falta método                             |
| POST   | `/auth/logout`                                               | Identity  | —                     | KEEP_EXACT          |                                              |
| GET    | `/auth/me`                                                   | Identity  | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/orders`                                                    | Orders    | —                     | KEEP_EXACT          | query: state, branchId                       |
| GET    | `/orders/:id`                                                | Orders    | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/orders`                                                    | Orders    | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/orders/:id/approve`                                        | Orders    | JWT                   | **BEHAVIOR_CHANGE** | 409 si sin stock; APPROVED garantiza reserva |
| POST   | `/orders/:id/cancel`                                         | Orders    | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/orders/:id/requeue-for-delivery`                           | Orders    | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/inventory/stock`                                           | Inventory | —                     | KEEP_EXACT          |                                              |
| POST   | `/inventory/stock/adjust`                                    | Inventory | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/inventory/stock/transfers`                                 | Inventory | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/inventory/stock/transfers`                                 | Inventory | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/inventory/stock/transfers/:id/receive`                     | Inventory | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/inventory/stock/transfers/:id/cancel`                      | Inventory | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/warehouse/picking-tasks`                                   | Warehouse | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/warehouse/picking-tasks/:id/complete`                      | Warehouse | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/logistics/delivery-route-candidates`                       | Logistics | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/logistics/delivery-routes`                                 | Logistics | JWT                   | KEEP_EXACT          |                                              |
| GET    | `/logistics/delivery-routes/:id/stops`                       | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-routes`                                 | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-routes/:id/assign-orders`               | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-routes/:id/dispatch`                    | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-route-stops/:id/proof-uploads`          | Logistics | JWT                   | KEEP_EXACT          | presigned MinIO                              |
| POST   | `/logistics/delivery-route-stops/:id/confirm-delivery`       | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-route-stops/:id/report-failed-delivery` | Logistics | JWT                   | KEEP_EXACT          |                                              |
| POST   | `/logistics/delivery-routes/:id/liquidate`                   | Logistics | JWT + **LIQUIDATION** | KEEP_EXACT          |                                              |
| POST   | `/sales/sessions/open`                                       | Sales     | JWT + **POS_RETAIL**  | KEEP_EXACT          |                                              |
| GET    | `/sales/sessions/current`                                    | Sales     | JWT + **POS_RETAIL**  | KEEP_EXACT          |                                              |
| GET    | `/sales/sessions/:id`                                        | Sales     | JWT + **POS_RETAIL**  | KEEP_EXACT          |                                              |
| POST   | `/sales/sessions/:id/sales`                                  | Sales     | JWT + **POS_RETAIL**  | KEEP_EXACT          | `payments[]` obligatorio                     |
| POST   | `/sales/sessions/:id/close`                                  | Sales     | JWT + **POS_RETAIL**  | KEEP_EXACT          |                                              |

**Total:** 37 rutas.

### 3.3 Contratos internos (no HTTP, misma TX)

| API                                           | Caller | Callee    | TX     |
| --------------------------------------------- | ------ | --------- | ------ |
| `IInventoryReservationApi.TryReserveForOrder` | Orders | Inventory | Shared |
| `IInventorySaleApi.DecrementForSale`          | Sales  | Inventory | Shared |

OpenAPI **no** documenta estas APIs; son contratos C# en `Binexus.Modules.*.Contracts`.

---

## 4. Semántica Orders — APPROVED

### 4.1 NestJS (actual)

```text
POST /orders/:id/approve
  → 200, state=APPROVED
  → outbox ORDER_APPROVED
  → (si dispatch) reserva async
  → si falla: INVENTORY_RESERVATION_FAILED → auto-cancel
```

`APPROVED` **no** implica stock reservado al responder.

### 4.2 .NET (objetivo — Opción A)

```text
POST /orders/:id/approve
  → TX: reserva sync
  → si ok: 200, state=APPROVED, reservas ACTIVE
  → si fail: 409 INSUFFICIENT_STOCK, state=DRAFT
```

| Estado      | Garantía                             |
| ----------- | ------------------------------------ |
| `DRAFT`     | Sin reserva                          |
| `APPROVED`  | Reserva ACTIVE para todas las líneas |
| `CANCELLED` | Reservas RELEASED                    |

Eventos post-commit: `ORDER_APPROVED`, `INVENTORY_RESERVED` (outbox).

**Actualizar:** `docs/states/order.md` en Etapa Orders.

---

## 5. Inventario de eventos (22)

Envelope común (`DomainEvent`):

```text
id, name, tenantId, occurredAt (ISO), version, payload, correlationId?, causationId?
```

| #   | Evento                         | Productor principal | Consumidores Nest hoy        | Schema Zod |
| --- | ------------------------------ | ------------------- | ---------------------------- | ---------- |
| 1   | USER_REGISTERED                | Identity (seed)     | —                            | ✓          |
| 2   | ORDER_CREATED                  | Orders              | Audit                        | ✓          |
| 3   | ORDER_APPROVED                 | Orders              | Inventory (async), Audit     | ✓          |
| 4   | ORDER_CANCELLED                | Orders              | Inventory release, Logistics | ✓          |
| 5   | INVENTORY_RESERVED             | Inventory           | Orders→Picking               | ✓          |
| 6   | INVENTORY_RESERVATION_FAILED   | Inventory           | Orders cancel                | ✓          |
| 7   | INVENTORY_RELEASED             | Inventory           | —                            | ✓          |
| 8   | ORDER_PICKING_STARTED          | Orders              | Warehouse                    | ✓          |
| 9   | PICKING_COMPLETED              | Warehouse           | Orders                       | ✓          |
| 10  | ORDER_READY_FOR_DELIVERY_ROUTE | Orders              | Logistics candidate          | ✓          |
| 11  | ORDER_DELIVERED                | Orders              | —                            | ✓          |
| 12  | ORDER_SETTLED                  | Orders              | —                            | ✓          |
| 13  | DELIVERY_ROUTE_CREATED         | Logistics           | —                            | ✓          |
| 14  | DELIVERY_ROUTE_ASSIGNED        | Logistics           | —                            | ✓          |
| 15  | DELIVERY_ROUTE_DISPATCHED      | Logistics           | Orders                       | ✓          |
| 16  | DELIVERY_CONFIRMED             | Logistics           | Orders                       | ✓          |
| 17  | DELIVERY_FAILED                | Logistics           | Orders                       | ✓          |
| 18  | DELIVERY_ROUTE_LIQUIDATED      | Logistics           | —                            | ✓          |
| 19  | SALES_SESSION_OPENED           | Sales               | —                            | ✓          |
| 20  | SALES_SESSION_CLOSED           | Sales               | —                            | ✓          |
| 21  | SALE_CREATED                   | Sales               | —                            | ✓          |
| 22  | PAYMENT_REGISTERED             | — (schema only)     | —                            | ✓          |

**Migración .NET:**

- Schemas JSON versionados en `backend/contracts/events/` (no OpenAPI).
- `ORDER_APPROVED` + reserva sync: `INVENTORY_RESERVED` puede emitirse en misma TX o inmediatamente post-commit.
- `INVENTORY_RESERVATION_FAILED` en approve sync: **no debería emitirse** en camino feliz; queda para compensaciones async futuras.

---

## 6. Matriz feature enforcement

| Feature key      | Seed default     | Enforcement Nest actual                       | Propuesto .NET Etapa 1    | Compatible |
| ---------------- | ---------------- | --------------------------------------------- | ------------------------- | ---------- |
| `POS_RETAIL`     | `enabled: false` | **Class** `SalesController` → 403 si disabled | Igual: todo `/sales/*`    | ✓          |
| `LIQUIDATION`    | `enabled: false` | Solo `POST .../liquidate`                     | Igual                     | ✓          |
| `POS_RESTAURANT` | false            | Ninguno                                       | Ninguno                   | ✓          |
| `ORDERS`         | false            | Ninguno                                       | **Ninguno** (no bloquear) | ✓          |
| `INVENTORY`      | false            | Ninguno                                       | Ninguno                   | ✓          |
| `WAREHOUSE_LITE` | false            | Ninguno                                       | Ninguno                   | ✓          |
| `ROUTES`         | false            | Ninguno                                       | Ninguno                   | ✓          |
| `BILLING`        | false            | Ninguno                                       | Ninguno                   | ✓          |
| `ANALYTICS`      | false            | Ninguno                                       | Ninguno                   | ✓          |

**Riesgo:** seed crea flags disabled; dev habilita `POS_RETAIL` manualmente. Documentar en README migración: script seed .NET debe replicar flags del tenant demo.

**No introducir** enforcement nuevo en `ORDERS`, `INVENTORY`, etc. sin fila explícita aprobada.

---

## 7. Inventario de pruebas de caracterización

### 7.1 Existentes (51 unit + 1 integration)

| Área            | Archivos | Cobertura                           |
| --------------- | -------- | ----------------------------------- |
| Orders commands | 10       | approve sin inventario real         |
| Orders events   | 5        | handlers mockeados                  |
| Inventory       | 6        | reserva en unit, no TX cross-module |
| Warehouse       | 2        |                                     |
| Logistics       | 12       | liquidación, COD, MinIO unit        |
| Sales           | 6        | split payment, arqueo query         |
| Platform        | 8        | outbox dispatcher **solo unit**     |
| Integration     | 1        | MinIO presigned (7 casos)           |

### 7.2 Faltantes — prioridad P0 (antes de cortar Nest)

| #   | Prueba                                    | Tipo        | Propósito                 |
| --- | ----------------------------------------- | ----------- | ------------------------- |
| 1   | Approve order + stock suficiente          | Integration | APPROVED + reserva ACTIVE |
| 2   | Approve order + stock insuficiente        | Integration | 409 + DRAFT               |
| 3   | Create sale + decrement stock             | Integration | Sales–Inventory sync TX   |
| 4   | Split payment sum mismatch                | Integration | 400                       |
| 5   | Close session arqueo CASH                 | Integration | expected vs declared      |
| 6   | Outbox: handler 1 ok, crash antes de 2    | Integration | inbox dedupe + reclaim    |
| 7   | Outbox: duplicate delivery                | Integration | idempotencia              |
| 8   | Outbox: lock expirado, 2 workers          | Integration | single winner             |
| 9   | Tenant A no lee datos Tenant B            | Integration | isolation                 |
| 10  | Auth login + refresh + me                 | Integration | paridad JWT               |
| 11  | POS_RETAIL disabled → 403                 | Integration | feature parity            |
| 12  | LIQUIDATION disabled → 403 solo liquidate | Integration |                           |

### 7.3 Faltantes — P1 (durante migración por módulo)

| #   | Prueba                                      |
| --- | ------------------------------------------- |
| 13  | Cancel order libera reservas                |
| 14  | Picking complete → READY_FOR_DELIVERY_ROUTE |
| 15  | Dispatch route → OUT_FOR_DELIVERY           |
| 16  | Confirm delivery + proof keys               |
| 17  | Failed delivery → DELIVERY_ATTEMPT_FAILED   |
| 18  | Requeue for delivery                        |
| 19  | Stock transfer receive/cancel               |
| 20  | Open session: duplicate OPEN terminal → 409 |

### 7.4 Estrategia

1. Portar tests unitarios existentes a xUnit (characterization).
2. Añadir P0 en `Binexus.IntegrationTests` con Testcontainers PostgreSQL.
3. Contract test OpenAPI vs rutas reales en CI.
4. Golden files para payloads de eventos críticos.

---

## 8. OpenAPI → SDK TypeScript

| Artefacto                     | Acción Etapa 0                                          |
| ----------------------------- | ------------------------------------------------------- |
| `openapi.json`                | No existe aún; generar en Gate 2                        |
| `packages/sdk/src/generated/` | Crear en pipeline CI post-scaffold                      |
| `packages/types`              | Mantener enums de dominio; DTOs HTTP migran a generated |
| SDK C#                        | **No** — capacidad futura documentada                   |

---

## 9. Checklist Etapa 0

| Item                               | Estado                                  |
| ---------------------------------- | --------------------------------------- |
| Documento arquitectura actualizado | ✓ `docs/architecture/dotnet-backend.md` |
| Matriz Prisma → EF                 | ✓ §2                                    |
| Inventario HTTP                    | ✓ §3                                    |
| Inventario eventos                 | ✓ §5                                    |
| Gap tests caracterización          | ✓ §7                                    |
| Semántica APPROVED                 | ✓ §4                                    |
| Matriz feature flags               | ✓ §6                                    |
| Política monetaria                 | ✓ arquitectura §7                       |
| Scaffold .NET                      | **NO** — esperar aprobación             |

---

## 10. Gate 2 — próximos pasos (tras aprobación)

1. `backend/Binexus.sln` + proyectos vacíos
2. `BinexusDbContext` + primera migración EF (schema completo)
3. `Binexus.ArchitectureTests` con reglas namespace
4. Pipeline OpenAPI → SDK
5. Testcontainers + primer test P0 (health + auth)

**No eliminar Nest** hasta checklist de paridad por módulo.
