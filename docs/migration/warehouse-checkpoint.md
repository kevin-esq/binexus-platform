# CHECKPOINT WAREHOUSE

**Fecha:** 2026-07-12  
**Estado:** **cerrado formalmente** — suite Release 160/160 verde.  
**Siguiente:** Logistics autorizado en el mismo PR.

---

## 1. Trigger canónico

Warehouse crea `PickingTask` solo al consumir `ORDER_APPROVED`.

| Evento                  | Rol en Warehouse                                                                            |
| ----------------------- | ------------------------------------------------------------------------------------------- |
| `ORDER_APPROVED`        | Crea `PickingTask` + líneas desde snapshot de la orden y solicita `MoveToPicking` a Orders. |
| `INVENTORY_RESERVED`    | No crea tareas. Es un hecho de Inventory.                                                   |
| `ORDER_PICKING_STARTED` | No crea tareas en .NET. Orders lo emite después de `MoveToPicking`.                         |

`ORDER_APPROVED` usa payload v1 estricto: `tenantId`, `orderId`, `branchId`, `eventId`, `actorId?` y `lines[]` con `orderLineId`, `productId`, `quantity`. `eventId` debe coincidir con el id del outbox message. Warehouse no lee `Orders.Domain`.

---

## 2. Transacción del handler

`OrderApprovedWarehouseProcessor` implementa `IIntegrationEventProcessor`.

El `OutboxProcessor` envuelve la entrega del handler en una transacción que cubre:

1. `PickingTask` + `PickingLine`.
2. `IOrderFulfillmentApi.MoveToPickingAsync`.
3. `EventHandlerDelivery = Processed` o `ProcessedIgnored`.

Si el handler falla antes del commit, la tarea y la marca Inbox se revierten juntas. `ProcessedIgnored` cierra casos esperados como orden inexistente o estado no aplicable y guarda código/mensaje sanitizados en la entrega.

---

## 3. Contrato Orders

`Binexus.Modules.Orders.Contracts` contiene solo:

```csharp
namespace Binexus.Modules.Orders.Contracts;

public enum OrderFulfillmentOutcome
{
    Success,
    AlreadyApplied,
    NoLongerApplicable,
    NotFound,
    ConcurrencyConflict,
}

public sealed record OrderFulfillmentResult(OrderFulfillmentOutcome Outcome, string? Code = null, string? Message = null);

public sealed record OrderFulfillmentRequest(
    Guid TenantId,
    Guid OrderId,
    Guid? ActorId,
    Guid? CorrelationId,
    Guid CausationId,
    string? Reason = null,
    string? Source = null);

public interface IOrderFulfillmentApi
{
    Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct);
    Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct);
}
```

Orders implementa el contrato en Infrastructure sin llamar `SaveChanges`. Warehouse referencia `Orders.Contracts`, no `Orders.Domain`, `Orders.Application`, `Orders.Infrastructure` ni `Orders.Features`.

---

## 4. Completar picking

`POST /warehouse/picking-tasks/{id}/complete` ejecuta `CompletePickingTaskCommand`.

La transacción cubre:

1. `PickingTask.Status = COMPLETED`.
2. `PickingLine.PickedQuantity = Quantity`.
3. `IOrderFulfillmentApi.MarkReadyForDeliveryRouteAsync`.
4. Outbox `PICKING_COMPLETED` informativo.

Orders queda en `READY_FOR_DELIVERY_ROUTE` en la misma transacción. No se registra un processor de Orders para `PICKING_COMPLETED` en .NET. `Idempotency-Key` se persiste como `warehouse-complete:{tenantId}:{key}` en `PickingTask.CompletionOperationKey`; repetir la misma key sobre la misma tarea ya completada devuelve éxito.

---

## 5. Esquema

```text
picking_tasks
  Id UUIDv7
  TenantId
  BranchId
  OrderId
  Status: PENDING | COMPLETED | CANCELLED
  CreatedFromEventId
  CompletedAtUtc?
  CompletedByUserId?
  CompletionOperationKey?
  Version xmin
  CreatedAtUtc
  UpdatedAtUtc

picking_lines
  Id
  TenantId
  PickingTaskId
  OrderLineId
  ProductId
  Quantity
  PickedQuantity
```

Índices y restricciones:

| Tabla           | Restricción                                                         |
| --------------- | ------------------------------------------------------------------- |
| `picking_tasks` | unique `(TenantId, OrderId)`                                        |
| `picking_tasks` | unique `(TenantId, CreatedFromEventId)`                             |
| `picking_tasks` | unique filtered `(TenantId, CompletionOperationKey)`                |
| `picking_tasks` | index `(TenantId, Status)`                                          |
| `picking_tasks` | index `(TenantId, BranchId)`                                        |
| `picking_lines` | unique `(TenantId, PickingTaskId, OrderLineId)`                     |
| `picking_lines` | `quantity > 0`, `pickedQuantity >= 0`, `pickedQuantity <= quantity` |

FK a tenants usa `Restrict`. Líneas hacen cascade con la tarea.

---

## 6. Verificación

```text
dotnet build -c Release → 0 warnings / 0 errors
dotnet test  -c Release → 160/160 (Unit 41 + Arch 22 + Integration 97)
dotnet package list --vulnerable → limpio
has-pending-model-changes → limpio
OpenAPI/SDK regenerados
```

Cobertura:

- `ORDER_APPROVED` crea una tarea.
- Retry / duplicado no crea otra tarea.
- Orden cancelada o inexistente al consumir `ORDER_APPROVED` termina `ProcessedIgnored` sin tarea.
- Payload con tenant incorrecto y tarea existente con branch distinto terminan permanente.
- Concurrency conflict de Orders reintenta y luego procesa.
- Falla antes del commit revierte tarea + Inbox.
- Complete solo acepta `PENDING`.
- Complete concurrente deja un ganador.
- Complete con la misma `Idempotency-Key` devuelve el mismo éxito.
- Complete con Orders fuera de `PICKING` revierte tarea y outbox.
- Aislamiento de tenant.
- Architecture tests protegen el contrato Orders y que no exista processor para `PICKING_COMPLETED`.

---

## 7. Riesgos

1. Nest emitía `PICKING_COMPLETED` y Orders lo consumía async; .NET completa + `MarkReady` en la misma TX HTTP (más atómico; divergencia documentada).
2. Status `CANCELLED` en enum/schema queda reservado; este cierre no agrega productor HTTP ni processor.
3. Logistics no se inicia en este cambio.

**Warehouse cerrado formalmente. Logistics autorizado.**
