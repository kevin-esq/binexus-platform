# Sales bounded context

Status: **active (F5.1)**.

Domain reference: [`docs/domains/sales.md`](../../../../../docs/domains/sales.md). ADR: [`docs/adr/0013-sales-pos-sub-slices-and-session-model.md`](../../../../../docs/adr/0013-sales-pos-sub-slices-and-session-model.md).

Implemented:

- `SalesModule` in `AppModule`
- Commands: `OpenSalesSession`, `CreateSale`, `CloseSalesSession`
- HTTP `/sales/*` gated by `POS_RETAIL`
- Web UI `/pos`
- Events: `SALES_SESSION_OPENED`, `SALES_SESSION_CLOSED`, `SALE_CREATED`, `PAYMENT_REGISTERED`
- Stock decrement inline on sale (`StockMovementType.SALE`)

Deferred (5.2+): split payment, credit, delivery orders, void/returns, Terminal catalog.

```txt
sales/
├── sales.module.ts
├── application/
│   ├── commands/
│   ├── sales-read.service.ts
│   └── session-cash-expected.ts
└── presentation/
    └── sales.controller.ts
```
