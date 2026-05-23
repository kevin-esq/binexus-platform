# Customers bounded context

Status: **placeholder** (Phase 1+).

Domain reference: [`docs/domains/customers.md`](../../../../../docs/domains/customers.md).

Customers owns customer identity, contacts, addresses, segments, and credit profile. Orders and Billing store snapshots where historical correctness matters.

Planned structure:

```txt
customers/
├── customers.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
