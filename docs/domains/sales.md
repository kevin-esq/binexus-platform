# Sales domain

Status: **active (F5.2)** — slices 5.1–5.2 shipped; credit and delivery orders deferred. Bounded context: `sales`.

Sales owns point-of-sale flows: retail tickets, payment capture at sale time, cash-register sessions, and (later) returns. POS is downstream of the operational foundation.

## Owns

- `SalesSession` — cashier shift scoped to a **terminal label** within a branch (`terminalId` string; no Terminal catalog yet).
- `Ticket` — POS sale document before fiscal/billing processing.
- `TicketLine` — sold items with manual price snapshot (Catalog deferred).
- `PaymentCapture` — one or more payment rows per ticket (split payment in 5.2).

Deferred past 5.2:

- `CashDrawerMovement` — cash in/out during a session.
- `ReturnRequest` — POS-originated return flow.

## Does not own

- Product definitions and prices. Those belong to [`catalog`](catalog.md) (5.1+ uses manual snapshots like Orders).
- Stock truth. That belongs to [`inventory`](inventory.md) (decremented inline on sale).
- Invoices/receivables. Those belong to [`billing`](billing.md).
- Order lifecycle for delivery/preorder flows. That belongs to [`orders`](orders.md) until 5.4.

## Commands (implemented)

- `OpenSalesSessionCommand` — `openingFloatCents`, `terminalId`, optional `branchId`.
- `CreateSaleCommand` — walk-in ticket + lines + **N `PaymentCapture` rows**; stock decrement in same transaction.
- `CloseSalesSessionCommand` — simple arqueo (`expectedClosingCents` vs `declaredClosingCents`).

Planned:

- `VoidTicketCommand`.
- `CreateReturnRequestCommand`.

## CreateSale payment rules (5.2)

- Request body **requires** `payments: [{ method, amountCents }, ...]` (no silent default).
- At least one capture; **no fixed maximum** on method count.
- Each `amountCents` must be a positive integer.
- Allowed methods: `CASH | CARD | TRANSFER` (`POS_WALK_IN_PAYMENT_METHODS`). **`CREDIT` rejected** (5.3).
- `sum(payments.amountCents) === ticket.totalCents` exactly — walk-in must pay 100%; no pending balance.
- Emits **one `PAYMENT_REGISTERED` per capture**.

## Events emitted

Active:

- `SALES_SESSION_OPENED`.
- `SALES_SESSION_CLOSED`.
- `SALE_CREATED` (session, terminal, lines, **payments**).
- `PAYMENT_REGISTERED` (one per `PaymentCapture`).

Future:

- `TICKET_VOIDED`.
- `RETURN_REQUESTED`.

## Events consumed

None (stock decrement is inline). Potential future:

- `SKU_PRICE_CHANGED` from Catalog for cache refresh.
- `CUSTOMER_BLOCKED` from Customers for account-sale restrictions.

## HTTP API

All routes require JWT, `@RequireFeature(POS_RETAIL)`, and roles `CASHIER` | `ADMIN` | `SUPER_ADMIN`.

```
POST /sales/sessions/open
GET  /sales/sessions/current?terminalId=...&branchId=...
GET  /sales/sessions/:id
POST /sales/sessions/:id/sales
POST /sales/sessions/:id/close
```

`POST /sales/sessions/:id/sales` body:

```json
{
  "lines": [{ "productId", "productName", "quantity", "unitPriceCents" }],
  "payments": [{ "method": "CASH", "amountCents": 5000 }, { "method": "CARD", "amountCents": 5000 }],
  "currency": "MXN"
}
```

## Session rules

1. At most **one `OPEN` session per `(tenantId, branchId, terminalId)`**.
2. Multiple terminals on the same branch may have concurrent OPEN sessions.
3. `CreateSale` requires an OPEN session; all sales and payments are scoped to that session/terminal.
4. Walk-in norm: `customerLabel = 'walk-in'` (no Customers row).

## Close arqueo

- `expectedClosingCents = openingFloatCents + sum(CASH PaymentCapture in session)`.
- **Non-cash portions (`CARD`, `TRANSFER`) do not affect session cash expected** — e.g. a $100 ticket paid $50 CASH + $50 CARD adds only $50 to arqueo.
- Cashier may close when declared matches expected.
- Mismatch requires `ADMIN` or `SUPER_ADMIN` plus `discrepancyReason`.

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
