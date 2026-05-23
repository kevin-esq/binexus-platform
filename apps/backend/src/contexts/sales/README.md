# Sales bounded context

Status: **placeholder** (Phase 5).

Domain reference: [`docs/domains/sales.md`](../../../../../docs/domains/sales.md).

Sales owns POS sessions, tickets, payment capture, cash drawer movements, and POS-originated returns. It emits sale/payment facts instead of mutating Inventory or Billing directly.

Planned structure:

```txt
sales/
├── sales.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
