# CHECKPOINT ORDERS (cierre formal)

**Fecha:** 2026-07-12  
**Estado:** **cerrado formalmente** — suite Release verde.  
**Siguiente:** Warehouse autorizado en el mismo PR tras esta cierre.

Auditoría Nest: [`orders-nest-audit.md`](./orders-nest-audit.md).

---

## 1. Contratos Inventory fuera de SharedKernel

**Opción elegida: A — assembly dedicado**

```text
Binexus.Modules.Inventory.Contracts
```

Contiene solo records + `IInventoryReservationApi` / `IInventorySaleApi`. Sin Platform, EF ni SharedKernel.

| Consumidor                 | Referencia                  |
| -------------------------- | --------------------------- |
| Inventory (implementación) | Contracts                   |
| Orders                     | Contracts únicamente        |
| SharedKernel               | **sin** contratos de módulo |

ArchitectureTests: SharedKernel libre de Inventory/Orders/Warehouse/Logistics/Sales; Orders no referencia Inventory Domain/Infrastructure/Features; Contracts sin deps Binexus.

---

## 2. Concurrencia (PostgreSQL real)

| Caso              | Resultado documentado                                                                                                                            |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| Dual approve      | Exactamente 1 APPROVED, 1 reserva, 1 movimiento RESERVE, 1 ORDER_APPROVED. Perdedor: `409 CONCURRENCY_CONFLICT` o `409 INVALID_ORDER_TRANSITION` |
| Approve vs cancel | Solo APPROVED+reserva ACTIVE **o** CANCELLED sin reserva. Nunca CANCELLED+ACTIVE ni dobles terminales                                            |
| Dual cancel       | Una transición CANCELLED; release/movimientos/outbox sin duplicar                                                                                |
| Dual requeue      | Una transición READY_FOR_DELIVERY_ROUTE                                                                                                          |
| Inbox duplicado   | `PICKING_COMPLETED` / `DELIVERY_CONFIRMED` / `DELIVERY_FAILED` / `DELIVERY_ROUTE_LIQUIDATED` — mismo EventId+HandlerKey no muta dos veces        |

---

## 3. UUIDv7

Todas las entidades Orders (`Order`, `OrderLine`, `OrderTransition` incl. initial) usan `IIdGenerator.NewId()`. Dominio exige `transitionId` inyectado por el handler. Sin `Guid.NewGuid()` ni fallback silencioso. Tests de versión 7 en flujo HTTP.

---

## 4. Trigger canónico Warehouse

| Evento                | Significado                                           | Consumidor autorizado                                         | No debe provocar                                              |
| --------------------- | ----------------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------- |
| ORDER_APPROVED        | Order pasó a APPROVED **y** stock ya reservado (sync) | **Warehouse (único trigger de picking)**                      | Segunda tarea de picking; escuchar también INVENTORY_RESERVED |
| INVENTORY_RESERVED    | Hecho de inventario: reservas creadas                 | Auditoría / reporting / futuros consumidores no-orquestadores | Crear PickingTask; MoveToPicking                              |
| ORDER_PICKING_STARTED | Order pasó a PICKING                                  | Logistics/reporting futuros                                   | Crear PickingTask de nuevo (Nest lo hacía; .NET no)           |

**Decisión .NET:** Warehouse escucha **solo** `ORDER_APPROVED`.  
Nest hoy: `INVENTORY_RESERVED` → MoveToPicking → `ORDER_PICKING_STARTED` → Warehouse. Con reserva sync, `ORDER_APPROVED` ya garantiza stock; Warehouse crea `PickingTask` y solicita `MoveOrderToPicking` en la misma TX del handler (+ Inbox Processed).  
`ORDER_APPROVED` **no** implica automáticamente `PICKING` hasta que Warehouse confirme la tarea.

---

## 5. Ownership de comandos internos

Todos son **comandos de Orders** (dueño: módulo Orders), con handlers reales y reglas de máquina de estados:

| Comando                               | Dueño  | HTTP           | Uso                             |
| ------------------------------------- | ------ | -------------- | ------------------------------- |
| CreateOrderCommand                    | Orders | POST /orders   | sí                              |
| ApproveOrderCommand                   | Orders | POST …/approve | sí                              |
| CancelOrderCommand                    | Orders | POST …/cancel  | sí                              |
| RequeueFailedDeliveryOrderCommand     | Orders | POST …/requeue | sí                              |
| MoveOrderToPickingCommand             | Orders | no             | futuro Warehouse via dispatcher |
| MarkOrderReadyForDeliveryRouteCommand | Orders | no             | futuro evento picking completed |
| MarkOrderOutForDeliveryCommand        | Orders | no             | futuro Logistics                |
| MarkOrderDeliveredCommand             | Orders | no             | futuro Logistics                |
| MarkOrderDeliveryAttemptFailedCommand | Orders | no             | futuro Logistics                |
| SettleOrderCommand                    | Orders | no             | futuro liquidación              |

No hay handlers vacíos, servicios fake, ni comandos de Warehouse/Logistics dentro de Orders.

---

## 6. Cancel + reservas

| Estado                  | ReleaseForOrder                                                       | INVENTORY_RELEASED |
| ----------------------- | --------------------------------------------------------------------- | ------------------ |
| DRAFT                   | no                                                                    | no                 |
| APPROVED                | sí, una vez                                                           | sí                 |
| DELIVERY_ATTEMPT_FAILED | sí si hay ACTIVE (paridad Nest `ORDER_CANCELLED` → inventory release) | sí                 |

**Riesgo de negocio:** en DELIVERY_ATTEMPT_FAILED la mercancía pudo haber salido físicamente; Nest igual libera reservas ACTIVE porque Delivery no hace SALE. Conservado y marcado.

---

## 7. Idempotencia

Formato: `{commandType}:{tenantId}:{Idempotency-Key}`  
Misma clave + mismo payload → resultado equivalente.  
Misma clave + payload distinto → `409 IDEMPOTENCY_KEY_REUSED`.  
Cubierto: create, approve, cancel, requeue.

---

## 8. OrderTransition vs AuditLog

```text
OrderTransition — historial de estado del agregado (TenantId, OrderId, From/To,
  ActorId, OccurredAtUtc, CorrelationId?, Reason sanitizada, OperationKey?).
  No sustituye un security/audit log transversal.

AuditLog — fuera de alcance de Orders.
```

---

## 9. Montos

`LineTotalCents` / `TotalCents` con `checked`. Cliente no envía total. Tests: cero líneas, qty 0, precio negativo, overflow línea/total, currency inválida.

---

## 10. Migraciones / verificación

- `Orders_Initial`, `Orders_TransitionTenantCorrelation`
- `has-pending-model-changes` → limpio

```text
dotnet restore → OK
dotnet build -c Release → 0 warnings / 0 errors
dotnet test  -c Release → 139/139 (Unit 38 + Arch 17 + Integration 84)
dotnet package list --vulnerable → limpio
OpenAPI/SDK regenerados
```

---

## Riesgos menores pendientes

1. Unique PG race en approve mapeada a `CONCURRENCY_CONFLICT` (aceptable).
2. Transition IDs UUIDv7 vía handler; CreateVersion7 en unit tests de dominio solo como stubs de id.
3. Warehouse aún no cableado a `ORDER_APPROVED`.

**Orders cerrado. Warehouse autorizado.**
