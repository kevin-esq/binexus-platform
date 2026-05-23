# Warehouse domain

Status: **planned** (Phase 3). Bounded context: `warehouse`.

Warehouse owns operational execution inside a branch/warehouse: picking, packing, staging, and handoff. It is intentionally warehouse-lite, not a full WMS.

## Owns

- `PickingTask` - work assigned to pick order lines.
- `PickingLine` - SKU/quantity to pick.
- `PackingTask` - optional packing step.
- `StagingArea` - where ready orders wait for route/customer pickup.
- `WarehouseException` - short pick, damaged product, missing SKU.

## Does not own

- Stock ledger. That belongs to [`inventory`](inventory.md).
- Order state machine. That belongs to [`orders`](orders.md).
- Route dispatch. That belongs to [`logistics`](logistics.md).

## Commands

Planned:

- `GeneratePickingTaskCommand`.
- `AssignPickerCommand`.
- `StartPickingCommand`.
- `ConfirmPickedLineCommand`.
- `ReportPickingExceptionCommand`.
- `MarkPickingCompleteCommand`.
- `StageOrderCommand`.

## Events emitted

Planned:

- `PICKING_TASK_CREATED`.
- `PICKING_STARTED`.
- `PICKING_EXCEPTION_REPORTED`.
- `PICKING_COMPLETED`.
- `ORDER_STAGED`.

## Events consumed

- `INVENTORY_RESERVED` from Inventory - generate picking tasks.
- `ORDER_CANCELLED` from Orders - cancel or stop pending picking.
- `ROUTE_ASSIGNED` from Logistics - move staged order into dispatch queue.

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
- Do staged orders belong to Warehouse until route dispatch or to Logistics once assigned?
