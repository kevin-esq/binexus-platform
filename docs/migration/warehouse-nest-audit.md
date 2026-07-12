> **KEEP_HISTORICAL** — Nest deleted in Gate 7 (ADR-0015). Audit matrix kept for migration history.

# Warehouse — auditoría Nest → .NET (pre-implementación)

**Fecha:** 2026-07-12  
**Fuentes:** `apps/backend/src/contexts/warehouse/**`, Prisma `PickingTask`/`PickingLine`, Orders inventory-reserved/picking handlers, `packages/types/src/warehouse.ts`

## Trigger Nest vs .NET

|             | Nest                                                                                                          | .NET (decidido)                                                                                              |
| ----------- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Cadena      | ORDER_APPROVED → reserve async → INVENTORY_RESERVED → MoveToPicking → **ORDER_PICKING_STARTED** → create task | ORDER_APPROVED (reserva ya sync) → **único trigger Warehouse** → create PickingTask + MoveToPicking misma TX |
| No escuchar | —                                                                                                             | INVENTORY_RESERVED para crear tarea                                                                          |

`ORDER_APPROVED` no implica PICKING hasta que el handler Warehouse lo solicite.

## Modelo Nest (Prisma)

```text
PickingTask: id, tenantId, orderId, branchId, status (PENDING|COMPLETED),
  createdFromEventId?, completedAt?, completedByUserId?, createdAt, updatedAt
  unique(orderId) implícito vía findFirst idempotente; 1:1 Order

PickingLine: id, tenantId, pickingTaskId, orderLineId, productId,
  quantity, pickedQuantity
  unique(tenantId, pickingTaskId, orderLineId)
```

Sin picking parcial en complete: al completar, `pickedQuantity = quantity` en todas las líneas.

## HTTP Nest

- `GET /warehouse/picking-tasks?limit&cursor&status?`
- `POST /warehouse/picking-tasks/:id/complete`

## Estados

| From    | Command/Event         | To           | Side effect                                      | Event                              |
| ------- | --------------------- | ------------ | ------------------------------------------------ | ---------------------------------- |
| —       | ORDER_APPROVED (.NET) | PENDING task | lines from order; Orders → PICKING               | ORDER_PICKING_STARTED (via Orders) |
| PENDING | Complete              | COMPLETED    | pickedQty=qty; Orders → READY_FOR_DELIVERY_ROUTE | PICKING_COMPLETED                  |

## Eventos

| Evento                | Rol Nest              | Rol .NET                                             |
| --------------------- | --------------------- | ---------------------------------------------------- |
| ORDER_APPROVED        | indirecto             | **trigger canónico Warehouse**                       |
| ORDER_PICKING_STARTED | trigger Warehouse     | emitido por Orders tras MoveToPicking; no crea tarea |
| PICKING_COMPLETED     | outbox complete       | outbox + Orders MarkReady                            |
| INVENTORY_RESERVED    | mueve Order a PICKING | auditoría only                                       |

## Tests Nest

- `warehouse-picking.service.spec.ts` — create idempotent
- `complete-picking-task.command.spec.ts` — complete + reject non-pending

## Exclusiones

Picker assignment, parcial, olas, zonas, barcode, packing, Logistics, mobile.
