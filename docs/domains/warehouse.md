# Warehouse domain

Status: **active** (Phase 3). Bounded context: `warehouse`.

Warehouse owns operational execution inside a branch/warehouse: picking, packing, staging, and handoff. It is intentionally warehouse-lite, not a full WMS.

## Owns

- `PickingTask` - work assigned to pick order lines.
- `PickingLine` - SKU/quantity to pick.
- `PackingTask` - optional packing step.
- `StagingArea` - where ready orders wait for delivery route / customer pickup.
- `WarehouseException` - short pick, damaged product, missing SKU.

## Does not own

- Stock ledger. That belongs to [`inventory`](inventory.md).
- Order state machine. That belongs to [`orders`](orders.md).
- Delivery route dispatch. That belongs to [`logistics`](logistics.md).

## Commands

Implemented:

- `CompletePickingTaskCommand` — marks a pending task complete, sets `pickedQuantity = quantity` on all lines, emits `PICKING_COMPLETED`.

Planned:

- `AssignPickerCommand`.
- `StartPickingCommand`.
- `ConfirmPickedLineCommand`.
- `ReportPickingExceptionCommand`.
- `StageOrderCommand`.

Picking task creation is event-driven via `ORDER_PICKING_STARTED` (no explicit create command in this slice).

## Events emitted

| Event               | When                                       |
| ------------------- | ------------------------------------------ |
| `PICKING_COMPLETED` | All lines picked; task marked `COMPLETED`. |

Planned: `PICKING_TASK_CREATED`, `PICKING_STARTED`, `PICKING_EXCEPTION_REPORTED`, `ORDER_STAGED`.

## Events consumed

| Event                   | Handler                               | Behavior                                              |
| ----------------------- | ------------------------------------- | ----------------------------------------------------- |
| `ORDER_PICKING_STARTED` | `OrderPickingStartedWarehouseHandler` | Create idempotent `PickingTask` + `PickingLine` rows. |

Planned: `ORDER_CANCELLED`, `DELIVERY_ROUTE_ASSIGNED`.

## HTTP surface

```txt
GET /warehouse/picking-tasks?status=PENDING&limit=50&cursor=
POST /warehouse/picking-tasks/:id/complete
```

## Web UI

- `/warehouse` — list pending picking tasks with **Complete** action.

## Implementation layout

```
apps/backend/src/contexts/warehouse/
  application/warehouse-picking.service.ts
  application/warehouse-read.service.ts
  application/commands/complete-picking-task.command.ts
  events/order-picking-started.handler.ts
  presentation/warehouse.controller.ts
  warehouse.module.ts
```

## Allowed dependencies

- May display product snapshots from Catalog, but should work from order/reservation snapshots.
- May tell Inventory what was physically picked through events.
- Must not decrement stock directly.

## Boundary rules

1. Warehouse executes physical work; Inventory owns stock truth.
2. Short-pick is an exception event, not an automatic order mutation.
3. Picking tasks are branch-scoped and user-auditable.
4. Keep Phase 3 warehouse-lite. No bin optimization, wave picking, or advanced WMS until proven necessary.

## Open questions

- Do we need barcode scanning in the first warehouse slice?
- Do staged orders belong to Warehouse until delivery route dispatch or to Logistics once assigned?
