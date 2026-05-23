# Sales domain

Status: **planned** (Phase 5). Bounded context: `sales`.

Sales owns point-of-sale flows: retail tickets, restaurant checks, payment capture at sale time, cash drawer/session state, and sale cancellation/returns. POS is downstream of the operational foundation, not the first module.

## Owns

- `SalesSession` - cashier/terminal shift.
- `Ticket` - POS sale document before fiscal/billing processing.
- `TicketLine` - sold items with price/tax snapshot.
- `PaymentCapture` - payment collected at POS.
- `CashDrawerMovement` - cash in/out during a session.
- `ReturnRequest` - POS-originated return flow.

## Does not own

- Product definitions and prices. Those belong to [`catalog`](catalog.md).
- Stock truth. That belongs to [`inventory`](inventory.md).
- Invoices/receivables. Those belong to [`billing`](billing.md).
- Order lifecycle for delivery/preorder flows. That belongs to [`orders`](orders.md).

## Commands

Planned:

- `OpenSalesSessionCommand`.
- `CreateSaleCommand`.
- `RegisterPaymentCommand`.
- `VoidTicketCommand`.
- `CloseSalesSessionCommand`.
- `CreateReturnRequestCommand`.

## Events emitted

Already registered:

- `SALE_CREATED`.
- `PAYMENT_REGISTERED`.

Future:

- `SALES_SESSION_OPENED`.
- `SALES_SESSION_CLOSED`.
- `TICKET_VOIDED`.
- `RETURN_REQUESTED`.

## Events consumed

Potential future:

- `SKU_PRICE_CHANGED` from Catalog for cache refresh.
- `CUSTOMER_BLOCKED` from Customers for account-sale restrictions.

## Allowed dependencies

- May snapshot Catalog and Customer data at sale time.
- May emit stock/payment facts to Inventory and Billing.
- Must not mutate Inventory balances or Billing invoices directly.

## Boundary rules

1. POS must keep selling in poor connectivity. Commands are idempotent and branch-scoped.
2. Ticket lines are historical snapshots; later catalog changes do not alter tickets.
3. Payment capture in Sales is a fact. Billing decides allocation/receivable state.
4. Restaurant-specific flows extend Sales, not Orders, unless they become delivery/preorder workflows.

## Open questions

- Do restaurant tables/check splitting belong in Sales Phase 5 or a later vertical?
- Do POS returns reverse Inventory immediately or emit a return event for Inventory to process?
