# Catalog domain

Status: **planned** (Phase 1+). Bounded context: `catalog`.

Catalog owns the sellable and stockable definitions used by Orders, Sales, Inventory, and Warehouse. It is reference data: other contexts use catalog snapshots, but only Catalog mutates catalog records.

## Owns

- `Product` - commercial product definition.
- `Sku` - sellable/stockable variant.
- `UnitOfMeasure` - pieces, boxes, kilograms, liters, etc.
- `PriceList` and `PriceListItem` - tenant/branch/customer pricing.
- `TaxCategory` - fiscal/tax classification.
- `Barcode` - lookup identifiers for POS/scanning.

## Does not own

- Stock balances. Those belong to [`inventory`](inventory.md).
- Order-line price snapshots. Those belong to [`orders`](orders.md).
- POS ticket lines. Those belong to [`sales`](sales.md).

## Commands

Planned:

- `CreateProductCommand`.
- `UpdateProductCommand`.
- `CreateSkuCommand`.
- `AssignBarcodeCommand`.
- `SetPriceCommand`.
- `DeactivateSkuCommand`.

## Events emitted

Planned:

- `PRODUCT_CREATED`.
- `SKU_CREATED`.
- `SKU_PRICE_CHANGED`.
- `SKU_DEACTIVATED`.

## Events consumed

None required for Phase 1. Catalog is mostly upstream reference data.

## Allowed dependencies

- Orders and Sales may snapshot product name, sku, unit, price, and tax category at the moment of creation.
- Inventory may reference `skuId` as an identifier, but cannot modify Catalog rows.
- Reporting may consume Catalog events to enrich projections.

## Boundary rules

1. Catalog is the source of truth for product definitions, not for availability.
2. Orders must not calculate historical totals by reading live prices. They store price snapshots.
3. Sales must not mutate product definitions during checkout.
4. Inventory must not infer product status. If a SKU is inactive, Catalog emits an event and Inventory decides whether existing stock can still move.

## Open questions

- Do tenants need branch-specific catalogs or only branch-specific prices?
- Do we support weighted products in Phase 1 or defer until POS/warehouse hardware integration?
