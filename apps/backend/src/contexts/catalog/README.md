# Catalog bounded context

Status: **placeholder** (Phase 1+).

Domain reference: [`docs/domains/catalog.md`](../../../../../docs/domains/catalog.md).

Catalog owns products, SKUs, units, prices, taxes, and barcodes. Other contexts may snapshot catalog data, but only this context mutates catalog records.

Planned structure:

```txt
catalog/
├── catalog.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
