# Binexus backend — Arquitectura .NET 10

**Estado:** Aprobado — .NET 10 es el único backend ([ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md))  
**Reemplaza:** NestJS (eliminado en Gate 7)  
**Mantiene:** ADR-0002 (monolito modular), ADR-0003 (contratos offline-first), ADR-0005 (multi-tenant, enmendado)

---

## 1. Principios

- Monolito modular, un deployable, costuras claras para extracción futura.
- Vertical Slice Architecture dentro de cada módulo (`Features/<UseCase>/`).
- DDD pragmático: agregados donde hay invariantes; sin ceremonia de capas vacías.
- PostgreSQL + EF Core; IDs persistentes **UUID v7**; base limpia gobernada por EF.
- OpenAPI = fuente de verdad HTTP → SDK TypeScript generado.
- Integration events vía outbox/inbox con workers; sin broker externo en este PR.
- Equipo pequeño: pocas abstracciones, pipelines explícitos, architecture tests.

---

## 2. Estructura de solución

```text
apps/backend/
  Binexus.slnx
  src/
    Binexus.Api/
    Binexus.Workers/
    Binexus.Platform/
    Binexus.SharedKernel/
    Modules/
      Binexus.Modules.Identity/
      Binexus.Modules.Orders/
      Binexus.Modules.Inventory/
      Binexus.Modules.Warehouse/
      Binexus.Modules.Logistics/
      Binexus.Modules.Sales/
  tests/
    Binexus.UnitTests/
    Binexus.IntegrationTests/
    Binexus.ArchitectureTests/
    Binexus.ContractTests/
```

**Un assembly por módulo** (no 4 proyectos × 6 módulos). Carpetas internas con reglas por namespace (§7).

---

## 3. Mapa de agregados (corregido)

### 3.1 Sales — agregados separados

**`SalesSession`** (AR) — ciclo de caja, no contiene tickets en memoria.

| Campo                                                                          | Notas                  |
| ------------------------------------------------------------------------------ | ---------------------- |
| Id                                                                             | UUID v7                |
| TenantId, BranchId                                                             |                        |
| TerminalId                                                                     | VO string (ADR-0013)   |
| OpenedByUserId, ClosedByUserId?                                                |                        |
| Status                                                                         | OPEN \| CLOSED         |
| OpeningFloatMinorUnits                                                         | int + Currency         |
| OpenedAtUtc, ClosedAtUtc?                                                      |                        |
| ExpectedClosingMinorUnits?, DeclaredClosingMinorUnits?, DiscrepancyMinorUnits? | calculados al cierre   |
| RowVersion                                                                     | concurrencia optimista |

**Invariantes:** máximo un OPEN por `(TenantId, BranchId, TerminalId)` (índice único parcial). Solo OPEN acepta nuevas ventas.

**`Sale` (Ticket)** (AR) — transacción de venta independiente.

| Campo                        | Notas                             |
| ---------------------------- | --------------------------------- |
| Id                           | UUID v7                           |
| TenantId, SalesSessionId     | FK por ID, sin cargar sesión      |
| BranchId, TerminalId         | desnormalizado para consultas     |
| Lines                        | entidades hijas                   |
| PaymentCaptures              | entidades hijas (ver §3.2)        |
| TotalMinorUnits, Currency    |                                   |
| Status                       | COMPLETED (v1)                    |
| CashierUserId, CustomerLabel |                                   |
| CreatedAtUtc                 |                                   |
| RowVersion                   | opcional; independiente de sesión |

**Cierre de sesión:** `CloseSalesSession` agrega por **query** (`SUM` captures CASH por `sessionId`), no cargando todos los `Sale`. Misma semántica que `computeSessionCashExpected` actual.

### 3.2 PaymentCapture — hijo de Sale

**Decisión:** `PaymentCapture` es **entidad hija del agregado `Sale`**, no agregado raíz.

| Razón                | Detalle                                                           |
| -------------------- | ----------------------------------------------------------------- |
| Ciclo de vida        | Se crea y completa con el ticket; no existe sin venta             |
| Invariante           | Suma de captures == total del sale (F5.2)                         |
| `sessionId` en tabla | **Índice de consulta** para arqueo; no expande el AR SalesSession |

No hay operaciones que muten un capture sin el sale (reembolsos → slice futuro).

### 3.3 Otros agregados

| Módulo    | AR                             | Hijos / notas                    |
| --------- | ------------------------------ | -------------------------------- |
| Identity  | User, Tenant                   | RefreshToken entidad             |
| Orders    | Order                          | OrderLine, OrderTransition (log) |
| Inventory | StockItem (por branch+product) | StockMovement ledger append-only |
| Inventory | StockTransfer                  | —                                |
| Warehouse | PickingTask                    | PickingLine; 1 por order         |
| Logistics | DeliveryRoute                  | Stop, Proof, Liquidation         |

Referencias cross-aggregate: **solo ID**.

---

## 4. Orders → Inventory — semántica de APPROVED

### 4.1 Comportamiento actual (NestJS) — inspeccionado

```text
ApproveOrderHandler:
  TX → Order.state = APPROVED
     → outbox ORDER_APPROVED
     → commit
  (si dispatch existiera) InventoryReservationService.handleOrderApproved
```

`docs/states/order.md` dice: _"Reserves stock (event-driven)"_ pero el estado **ya es APPROVED** antes de reservar.

Si reserva falla: `INVENTORY_RESERVATION_FAILED` → **auto-cancel** desde APPROVED (`InventoryReservationFailedOrdersHandler`).

**Conclusión:** hoy `APPROVED` **no garantiza** stock reservado al responder HTTP. Es una brecha semántica.

### 4.2 Decisión .NET — Opción A (síncrona)

```text
ApproveOrderCommand:
  BEGIN TX
  → cargar Order (DRAFT)
  → IInventoryReservationApi.TryReserveForOrder(order)  // misma TX
  → si falla: ROLLBACK, HTTP 409 INSUFFICIENT_STOCK (Order sigue DRAFT)
  → si ok: Order → APPROVED, transition, outbox ORDER_APPROVED + INVENTORY_RESERVED
  → COMMIT
```

| Estado      | Significado                           |
| ----------- | ------------------------------------- |
| `DRAFT`     | Editable; sin compromiso de stock     |
| `APPROVED`  | **Stock reservado** (garantía fuerte) |
| `CANCELLED` | Libera reservas                       |

**No** se adopta `PENDING_INVENTORY` en este PR (Opción B) — evita cambio de SM y de contratos HTTP.

**Eventos post-commit:** `ORDER_PICKING_STARTED` y downstream siguen vía outbox (asíncrono aceptable).

### 4.3 Diferencia con Sales

| Caso               | Política                     | Por qué                              |
| ------------------ | ---------------------------- | ------------------------------------ |
| Sales → Inventory  | Sync API misma TX            | Caja exige respuesta inmediata       |
| Orders → Inventory | Sync API misma TX en approve | `APPROVED` debe ser verdad operativa |
| Orders → Warehouse | Async event                  | Picking puede esperar ms             |

### 4.4 Pruebas de integración obligatorias

1. Approve con stock suficiente → APPROVED + reservas ACTIVE + outbox.
2. Approve sin stock → 409 + permanece DRAFT + sin reserva.

---

## 5. Reglas de namespaces (ArchitectureTests)

Dentro de `Binexus.Modules.X`:

| Namespace / carpeta       | Puede referenciar                                                    |
| ------------------------- | -------------------------------------------------------------------- |
| `.Domain`                 | Solo SharedKernel (mínimo)                                           |
| `.Domain`                 | **NO** Application, Infrastructure, Contracts, Platform, EF, ASP.NET |
| `.Application`            | Domain, Contracts (interfaces públicas)                              |
| `.Application`            | **NO** Infrastructure concreta, DbContext                            |
| `.Infrastructure`         | Application contracts, Platform, EF Core                             |
| `.Features.*` (endpoints) | Application dispatchers, Contracts DTOs                              |
| `.Features.*`             | **NO** DbContext directo, **NO** reglas de dominio                   |

**Platform:** solo `Infrastructure` y `*ModuleRegistration.cs` del módulo importan Platform. Domain/Application no.

Tests: NetArchTest + reglas custom por namespace suffix.

---

## 6. SharedKernel — mínimo real

**Incluir:**

- `Result` / `Error` / `ErrorKind`
- `ITenantEntity` marker (opcional)
- Tipos ultra-estables si hace falta: ningún ID de módulo aquí

**Excluir (viven en módulos):**

- `OrderId` → Orders.Domain
- `RouteId`, `DeliveryRouteId` → Logistics.Domain
- `SalesSessionId`, `SaleId` → Sales.Domain
- `StockItemId` → Inventory.Domain

**Dinero:** no `Money` rico en SharedKernel todavía. Ver §12.

**Presupuesto:** tipos públicos solo los que justifiquen dependencia cross-module. Sin meta artificial de “25 tipos”.

---

## 7. Política monetaria (migración)

### 7.1 Compatibilidad v1

| Concepto           | Política                                                |
| ------------------ | ------------------------------------------------------- |
| Montos persistidos | `int` minor units (centavos MXN) — **igual que Prisma** |
| `double`           | Prohibido                                               |
| Currency           | `string` ISO 4217, 3 chars                              |
| Split payment      | Suma exacta == total; ya validado en F5.2               |

### 7.2 Documentado para futuro (no implementar en PR)

| Tema                            | Nota                                                    |
| ------------------------------- | ------------------------------------------------------- |
| Cantidades fraccionables / peso | `decimal` + unidad; no `int` quantity                   |
| Impuestos / descuentos          | líneas de ajuste; redondeo por política fiscal          |
| Redondeo split                  | último método absorbe residuo (definir en slice futuro) |
| Cálculos intermedios            | `decimal` en dominio; persistir minor units redondeados |

### 7.3 Pruebas migración

- Reconciliación arqueo sesión (CASH only).
- Split 50/50 y residuo 1 centavo.
- Liquidación ruta COD.

---

## 8. Unit of Work — decisión

**No** habrá `IUnitOfWork` genérico que reenvíe `SaveChanges` + `BeginTransaction`.

**Patrón concreto:**

```text
CommandDispatcher:
  await using var tx = await dbContext.Database.BeginTransactionAsync(ct)
  try {
    await handler.Handle(...)
    await dbContext.SaveChangesAsync(ct)
    await tx.CommitAsync(ct)
  } catch { await tx.RollbackAsync(ct); throw }
```

`BinexusDbContext` scoped por request/command. Handlers que participan en TX reciben el mismo scoped context.

Coordinador vive en **dispatcher**, no como interfaz reutilizable vacía.

---

## 9. Outbox / Inbox — garantías y transacciones

### 9.1 Garantías

| #   | Garantía                                                                                    |
| --- | ------------------------------------------------------------------------------------------- |
| G1  | Entrega **at-least-once**                                                                   |
| G2  | Progreso por handler **solo** en `EventHandlerDelivery` (inbox)                             |
| G3  | Handler **idempotente** vía UNIQUE `(TenantId, EventId, HandlerKey)`                        |
| G4  | Handler exitoso **no se re-ejecuta** aunque otro handler del mismo evento falle             |
| G5  | Outbox → `Completed` solo cuando todas las entregas del snapshot terminaron                 |
| G6  | Snapshot de handlers **inmutable** al primer claim; handlers nuevos no reprocesan histórico |
| G7  | Payload inválido → `FailedPermanent` (sin retry infinito)                                   |
| G8  | `LastError` sin payloads completos ni secretos                                              |
| G9  | Lock expirado → reclaim por otro worker                                                     |
| G10 | Worker **no** mantiene TX PostgreSQL abierta durante HTTP/MinIO                             |
| G11 | Tenant scope explícito en cada handler                                                      |

Ver diseño completo: [`docs/migration/gate-2a-outbox-inbox.md`](../migration/gate-2a-outbox-inbox.md).

### 9.2 Flujo transaccional worker

```text
1. Poll outbox (SKIP LOCKED) — sin TX larga
2. Por mensaje:
   a. BEGIN TX
   b. Para cada handler: si inbox processed → skip; else ejecutar handler; upsert inbox
   c. Si todos ok → mark outbox Published
   d. COMMIT
3. MinIO / HTTP: fuera de TX PostgreSQL (confirm-delivery ya separa upload de confirm)
```

### 9.3 Pruebas obligatorias

- Worker crash tras handler 1 de 2
- Handler duplicado (inbox dedupe)
- Lock expirado, dos workers
- Fallo transitorio vs permanente
- Tenant isolation en worker

---

## 10. Feature flags — matriz y migración

Ver [`docs/migration/BASELINE.md`](../migration/BASELINE.md) §6.

**Regla migración:** enforcement .NET = **igual que Nest hoy** para endpoints existentes. No bloquear módulos que hoy están abiertos. Nuevo enforcement → fila explícita “breaking” + aprobación.

Separar: **Entitlement** (tenant pagó) vs **Role** (usuario puede) vs **Branch/Terminal capability** (futuro).

---

## 11. OpenAPI y SDK

- ASP.NET Core genera OpenAPI 3.1.
- CI: `openapi.json` artifact + contract test.
- `pnpm generate:sdk` → `packages/sdk/src/generated/`.
- `packages/types`: enums de dominio expuestos al cliente hasta migración completa; DTOs HTTP deprecados.
- **Sin SDK C#** en este PR.
- Integration events: JSON Schema / C# records versionados, **no** OpenAPI.

---

## 12. Dispatcher (sin MediatR)

`ICommandDispatcher` + handlers registrados por DI. Pipeline explícito: logging → tenant → auth → entitlement → validation → idempotency → handler → save → outbox enqueue → commit.

Queries: `IQueryHandler` o métodos read service; sin outbox.

---

## 13. Observabilidad

Serilog + OpenTelemetry. CorrelationId de request → outbox → logs. Health: PG, MinIO, outbox lag. Sin collector obligatorio en dev.

---

## 14. ADRs pendientes de implementación

| ADR  | Tema                                         |
| ---- | -------------------------------------------- |
| 0014 | Migración backend .NET 10                    |
| 0015 | Sales–Inventory sync TX                      |
| 0016 | Estructura monolito modular .NET             |
| 0017 | DbContext único + configuraciones por módulo |
| 0018 | UUID v7                                      |
| 0019 | Outbox/Inbox                                 |
| 0020 | OpenAPI-first SDK                            |
| 0021 | Command dispatcher                           |
| 0022 | Entitlements vs RBAC                         |
| 0023 | Orders APPROVED + reserva síncrona           |
| 0024 | SalesSession y Sale como agregados separados |

---

## 15. Referencias

- [`docs/migration/BASELINE.md`](../migration/BASELINE.md) — Etapa 0
- [`docs/states/order.md`](../states/order.md) — actualizar al implementar Opción A
- [`docs/adr/0013-sales-pos-sub-slices-and-session-model.md`](../adr/0013-sales-pos-sub-slices-and-session-model.md)
