# Reporting bounded context

Status: **placeholder** (Phase 8+).

Domain reference: [`docs/domains/reporting.md`](../../../../../docs/domains/reporting.md).

Reporting owns projections, report snapshots, dashboard metrics, and analytics jobs. It consumes domain events and never owns operational truth.

Planned structure:

```txt
reporting/
├── reporting.module.ts
├── domain/
├── application/
├── infrastructure/
└── presentation/
```
