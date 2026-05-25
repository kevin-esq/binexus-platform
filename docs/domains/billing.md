# Billing domain

Status: **planned** (Phase 7). Bounded context: `billing`.

Billing owns financial documents and receivables: invoices, payment allocation, credits, and fiscal document lifecycle. It consumes operational facts from Orders, Sales, and Logistics, then creates financial truth.

## Owns

- `Invoice` - fiscal/commercial billing document.
- `InvoiceLine` - billed line item snapshot.
- `Receivable` - amount owed by customer.
- `PaymentAllocation` - payment applied to invoice/receivable.
- `CreditNote` - financial adjustment.
- `FiscalDocumentStatus` - generated/sent/cancelled state.

## Does not own

- POS payment capture. That belongs to [`sales`](sales.md).
- Delivery route cash collection proof. That belongs to [`logistics`](logistics.md).
- Customer credit policy metadata. That belongs to [`customers`](customers.md).
- Order state. That belongs to [`orders`](orders.md).

## Commands

Planned:

- `GenerateInvoiceCommand`.
- `RegisterReceivableCommand`.
- `AllocatePaymentCommand`.
- `IssueCreditNoteCommand`.
- `CancelInvoiceCommand`.
- `MarkInvoiceSentCommand`.

## Events emitted

Planned:

- `INVOICE_GENERATED`.
- `RECEIVABLE_CREATED`.
- `PAYMENT_ALLOCATED`.
- `CREDIT_NOTE_ISSUED`.
- `INVOICE_CANCELLED`.

## Events consumed

- `ORDER_DELIVERED` from Orders/Logistics - generate receivable/invoice when configured.
- `SALE_CREATED` from Sales - create sale invoice/receipt when configured.
- `PAYMENT_REGISTERED` from Sales/Logistics - allocate payment.
- `DELIVERY_ROUTE_LIQUIDATED` from Logistics - reconcile delivery route collections.

## Allowed dependencies

- May snapshot customer fiscal data from Customers or from the originating order/sale.
- May emit financial status events for Orders/Reporting.
- Must not change operational delivery/picking state.

## Boundary rules

1. Billing is financial truth; Sales/Logistics are collection/capture facts.
2. Fiscal integrations are infrastructure under Billing, not cross-cutting service calls from Sales.
3. Payment allocation must be idempotent by external/payment reference.
4. Financial documents are immutable once issued; corrections use credit notes/cancellations.

## Open questions

- Which country-specific fiscal provider comes first?
- Do we need accounts receivable aging before analytics, or can it wait until Reporting?
