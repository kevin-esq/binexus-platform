# Warehouse bounded context

Status: **placeholder** (Phase 3).

Domain reference: [`docs/domains/warehouse.md`](../../../../../docs/domains/warehouse.md).

Warehouse owns picking, packing, staging, and warehouse exceptions. It is warehouse-lite: no advanced WMS until proven necessary.

Planned structure:

```txt
warehouse/
├── warehouse.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
