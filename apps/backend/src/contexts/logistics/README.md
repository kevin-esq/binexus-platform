# Logistics bounded context

Status: **active** — F4 phase **complete** (planning through MinIO integration tests).

Domain reference: [`docs/domains/logistics.md`](../../../../../docs/domains/logistics.md).

Logistics owns delivery routes, dispatch, delivery confirmation, presigned proof uploads, failed delivery handling, and route liquidation.

Current structure:

```txt
logistics/
├── logistics.module.ts
├── application/
│   ├── commands/ (create route, assign, dispatch, confirm delivery, report failed, proof upload, liquidate)
│   ├── route-completion.ts
│   ├── route-cod-expected.ts
│   ├── logistics-candidate.service.ts  (ORDER_READY_FOR_DELIVERY_ROUTE + ORDER_CANCELLED + requeue)
│   └── logistics-read.service.ts
├── events/ (order-ready-for-delivery-route, order-cancelled)
└── presentation/logistics.controller.ts
```

Implemented: route planning, dispatch, confirm delivery, presigned proof uploads (MinIO hardening), failed delivery + resolution hooks, route liquidation (COD arqueo, `@RequireFeature(LIQUIDATION)`), read APIs under `/logistics/*`.

Integration tests: `apps/backend/src/__integration__/logistics/delivery-proof-minio.integration.spec.ts` (`pnpm test:integration`).
