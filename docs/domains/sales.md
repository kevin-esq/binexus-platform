# Sales domain

Status: **active (F5.1)** — slice 5.1 shipped; split payment, credit, and delivery orders deferred. Bounded context: `sales`.

Sales owns point-of-sale flows: retail tickets, payment capture at sale time, cash-register sessions, and (later) returns. POS is downstream of the operational foundation.

## Owns

- `SalesSession` — cashier shift scoped to a **terminal label** within a branch (`terminalId` string; no Terminal catalog in 5.1).
- `Ticket` — POS sale document before fiscal/billing processing.
- `TicketLine` — sold items with manual price snapshot (Catalog deferred).
- `PaymentCapture` — payment collected at POS (single `CASH` row in 5.1).

Deferred past 5.1:

- `CashDrawerMovement` — cash in/out during a session.
- `ReturnRequest` — POS-originated return flow.

## Does not own

- Product definitions and prices. Those belong to [`catalog`](catalog.md) (5.1 uses manual snapshots like Orders).
- Stock truth. That belongs to [`inventory`](inventory.md) (decremented inline on sale in 5.1).
- Invoices/receivables. Those belong to [`billing`](billing.md).
- Order lifecycle for delivery/preorder flows. That belongs to [`orders`](orders.md) until 5.4.

## Commands (implemented in 5.1)

- `OpenSalesSessionCommand` — `openingFloatCents`, `terminalId`, optional `branchId`.
- `CreateSaleCommand` — walk-in ticket + lines + single `CASH` capture; stock decrement in same transaction.
- `CloseSalesSessionCommand` — simple arqueo (`expectedClosingCents` vs `declaredClosingCents`).

Planned:

- `VoidTicketCommand`.
- `CreateReturnRequestCommand`.

## Events emitted

Active:

- `SALES_SESSION_OPENED`.
- `SALES_SESSION_CLOSED`.
- `SALE_CREATED` (extended payload: session, terminal, lines).
- `PAYMENT_REGISTERED`.

Future:

- `TICKET_VOIDED`.
- `RETURN_REQUESTED`.

## Events consumed

None in 5.1 (stock decrement is inline). Potential future:

- `SKU_PRICE_CHANGED` from Catalog for cache refresh.
- `CUSTOMER_BLOCKED` from Customers for account-sale restrictions.

## HTTP API (5.1)

All routes require JWT, `@RequireFeature(POS_RETAIL)`, and roles `CASHIER` | `ADMIN` | `SUPER_ADMIN`.

```
POST /sales/sessions/open
GET  /sales/sessions/current?terminalId=...&branchId=...
GET  /sales/sessions/:id
POST /sales/sessions/:id/sales
POST /sales/sessions/:id/close
```

`GET /sales/sessions/current` returns the OPEN session for `(branch, terminalId)` or `null`.

## Session rules

1. At most **one `OPEN` session per `(tenantId, branchId, terminalId)`** (partial unique index in DB).
2. Multiple terminals on the same branch may have concurrent OPEN sessions.
3. `CreateSale` requires an OPEN session; all sales and payments are scoped to that session/terminal.
4. Walk-in norm: `customerLabel = 'walk-in'` (no Customers row).

## Close arqueo (5.1)

- `expectedClosingCents = openingFloatCents + sum(CASH PaymentCapture in session)`.
- Cashier may close when declared matches expected.
- Mismatch requires `ADMIN` or `SUPER_ADMIN` plus `discrepancyReason` (shared helper with route liquidation).

## Allowed dependencies

- May snapshot product labels and prices at sale time.
- May decrement Inventory `onHand` in the sale transaction (`StockMovementType.SALE`).
- Must not mutate Billing invoices directly.

## Boundary rules

1. Ticket lines are historical snapshots; later catalog changes do not alter tickets.
2. Payment capture in Sales is a fact. Billing decides allocation/receivable state.
3. Restaurant-specific flows extend Sales, not Orders, unless they become delivery/preorder workflows.

## Open questions

- Do restaurant tables/check splitting belong in Sales Phase 5 or a later vertical?
- Do POS returns reverse Inventory immediately or emit a return event for Inventory to process?
- When do we introduce an admin-managed Terminal catalog vs free-form `terminalId` labels?
