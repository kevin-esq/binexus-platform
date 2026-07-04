# Logistics bounded context

Status: **active** (F4 · Logistics — through failed delivery #3).

Domain reference: [`docs/domains/logistics.md`](../../../../../docs/domains/logistics.md).

Logistics owns delivery routes, dispatch, delivery confirmation, failed delivery handling, and route liquidation.

Current structure:

```txt
logistics/
├── logistics.module.ts
├── application/
│   ├── commands/ (create route, assign, dispatch, confirm delivery, report failed, proof upload)
│   ├── route-completion.ts
│   ├── delivery-proof-object-key.ts
│   ├── logistics-read.service.ts
│   └── logistics-candidate.service.ts
└── presentation/logistics.controller.ts
```

Implemented: route planning, dispatch, confirm delivery with optional `DeliveryProof` (MinIO upload verification on confirm), presigned MinIO proof uploads, failed delivery report with `DELIVERY_FAILED`, terminal-stop route completion, read APIs under `/logistics/*`.

Planned: failed delivery resolution (#3b), route liquidation (#4).
