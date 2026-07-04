# Logistics bounded context

Status: **active** (F4 · Logistics — through proof base; presigned uploads next).

Domain reference: [`docs/domains/logistics.md`](../../../../../docs/domains/logistics.md).

Logistics owns delivery routes, dispatch, delivery confirmation, failed delivery handling, and route liquidation.

Current structure:

```txt
logistics/
├── logistics.module.ts
├── application/
│   ├── commands/ (create route, assign, dispatch, confirm delivery)
│   ├── logistics-read.service.ts
│   └── logistics-candidate.service.ts
└── presentation/logistics.controller.ts
```

Implemented: route planning, dispatch, confirm delivery with optional `DeliveryProof`, read APIs under `/logistics/*`.

Next slice: presigned MinIO proof uploads (`CreateDeliveryProofUploadCommand`).

Planned: failed delivery, route liquidation.
