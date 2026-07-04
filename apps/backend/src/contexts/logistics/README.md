# Logistics bounded context

Status: **active** (F4 · Logistics — through presigned proof uploads).

Domain reference: [`docs/domains/logistics.md`](../../../../../docs/domains/logistics.md).

Logistics owns delivery routes, dispatch, delivery confirmation, failed delivery handling, and route liquidation.

Current structure:

```txt
logistics/
├── logistics.module.ts
├── application/
│   ├── commands/ (create route, assign, dispatch, confirm delivery, proof upload)
│   ├── delivery-proof-object-key.ts
│   ├── logistics-read.service.ts
│   └── logistics-candidate.service.ts
└── presentation/logistics.controller.ts
```

Implemented: route planning, dispatch, confirm delivery with optional `DeliveryProof`, presigned MinIO proof uploads, read APIs under `/logistics/*`.

Planned: failed delivery, route liquidation.
