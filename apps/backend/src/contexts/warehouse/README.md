# Warehouse bounded context

Status: **active** (F3 · Warehouse — picking base).

Domain reference: [`docs/domains/warehouse.md`](../../../../../docs/domains/warehouse.md).

Warehouse owns picking, packing, staging, and warehouse exceptions. It is warehouse-lite: no advanced WMS until proven necessary.

Current structure:

```txt
warehouse/
├── warehouse.module.ts
├── application/
│   ├── commands/complete-picking-task.command.ts
│   ├── warehouse-picking.service.ts
│   └── warehouse-read.service.ts
├── events/order-picking-started.handler.ts
└── presentation/warehouse.controller.ts
```

Implemented: `CompletePickingTaskCommand`, `ORDER_PICKING_STARTED` consumer, `GET /warehouse/picking-tasks`, `POST /warehouse/picking-tasks/:id/complete`.

Planned: assign picker, per-line confirm, exceptions, staging commands.
