# Sales bounded context

Status: **active (F5.2)**.

Domain reference: [`docs/domains/sales.md`](../../../../../docs/domains/sales.md). ADR: [`docs/adr/0013-sales-pos-sub-slices-and-session-model.md`](../../../../../docs/adr/0013-sales-pos-sub-slices-and-session-model.md).

Implemented:

- `SalesModule` in `AppModule`
- Commands: `OpenSalesSession`, `CreateSale` (split payment), `CloseSalesSession`
- HTTP `/sales/*` gated by `POS_RETAIL`
- Web UI `/pos` with multi-line payment checkout
- Events: `SALES_SESSION_OPENED`, `SALES_SESSION_CLOSED`, `SALE_CREATED` (+ payments), N × `PAYMENT_REGISTERED`
- Stock decrement inline on sale (`StockMovementType.SALE`)
- Session arqueo: `computeSessionCashExpected` sums **CASH captures only**

Deferred (5.3+): credit, delivery orders, void/returns, Terminal catalog.

```txt
sales/
├── sales.module.ts
├── application/
│   ├── commands/
│   ├── validate-sale-payments.ts
│   ├── sales-read.service.ts
│   └── session-cash-expected.ts
└── presentation/
    └── sales.controller.ts
```
