# Logistics bounded context

Status: **active** (F4 · Logistics — through failed delivery resolution #3b).

Domain reference: [`docs/domains/logistics.md`](../../../../../docs/domains/logistics.md).

Logistics owns delivery routes, dispatch, delivery confirmation, failed delivery handling, and route liquidation.

Current structure:

```txt
logistics/
├── logistics.module.ts
├── application/
│   ├── commands/ (create route, assign, dispatch, confirm delivery, report failed, proof upload)
│   ├── route-completion.ts
│   ├── logistics-candidate.service.ts  (ORDER_READY_FOR_DELIVERY_ROUTE + ORDER_CANCELLED)
│   └── logistics-read.service.ts
├── events/ (order-ready-for-delivery-route, order-cancelled)
└── presentation/logistics.controller.ts
```

Implemented: route planning, dispatch, confirm delivery, presigned proof uploads, failed delivery, candidate requeue (`ASSIGNED → READY`) and cancel on order events, read APIs under `/logistics/*`.

Planned: route liquidation (#4).
