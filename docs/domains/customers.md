# Customers domain

Status: **planned** (Phase 1+). Bounded context: `customers`.

Customers owns the commercial relationship with buyers: identity, contacts, delivery/billing addresses, and credit profile. Orders and Billing depend on Customer snapshots, not live mutable rows.

## Owns

- `Customer` - buyer account within a tenant.
- `CustomerContact` - contact people and channels.
- `CustomerAddress` - delivery/billing addresses.
- `CustomerCreditProfile` - credit limit, payment terms, blocked status.
- `CustomerSegment` - grouping for prices and reporting.

## Does not own

- Invoices or receivables. Those belong to [`billing`](billing.md).
- Sales tickets. Those belong to [`sales`](sales.md).
- Orders placed by the customer. Those belong to [`orders`](orders.md).

## Commands

Planned:

- `CreateCustomerCommand`.
- `UpdateCustomerCommand`.
- `AddCustomerAddressCommand`.
- `SetCreditLimitCommand`.
- `BlockCustomerCommand`.
- `UnblockCustomerCommand`.

## Events emitted

Planned:

- `CUSTOMER_CREATED`.
- `CUSTOMER_UPDATED`.
- `CUSTOMER_CREDIT_CHANGED`.
- `CUSTOMER_BLOCKED`.

## Events consumed

Potential future:

- `INVOICE_OVERDUE` from Billing, to update risk/blocked status.

## Allowed dependencies

- Orders may snapshot customer name, billing identity, address, and credit terms.
- Billing may read immutable identifiers and fiscal identity snapshots from the order/invoice workflow.
- Reporting may consume customer events for segmentation.

## Boundary rules

1. Customer changes do not mutate old orders or invoices.
2. Credit decisions must be explicit commands/events, not ad-hoc checks buried in Orders.
3. Orders can ask Customers whether a customer is currently orderable, but that result must be captured in the order approval trail.
4. Billing owns balances; Customers owns credit policy metadata.

## Open questions

- Is credit approval required for all B2B orders or only tenants with credit enabled?
- Do walk-in POS customers create real customer rows or use a special anonymous customer snapshot?
