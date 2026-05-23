# Reporting domain

Status: **planned** (Phase 8+). Bounded context: `reporting`.

Reporting owns read models, analytics projections, and operational dashboards. It must never become the source of truth for operational decisions.

## Owns

- `Projection` - materialized view definition/state.
- `ReportSnapshot` - generated report output.
- `DashboardMetric` - cached metric values.
- `AnalyticsJob` - projection rebuild or scheduled report.

## Does not own

- Orders, stock, tickets, invoices, routes, or customers as source-of-truth data.
- Command-side validation for operational workflows.
- AI/forecasting models in Phase 1-7.

## Commands

Planned:

- `RebuildProjectionCommand`.
- `GenerateReportSnapshotCommand`.
- `RefreshDashboardMetricCommand`.

## Events emitted

Planned:

- `PROJECTION_REBUILT`.
- `REPORT_SNAPSHOT_CREATED`.

## Events consumed

Reporting consumes almost every stable domain event once that event has a schema/version policy:

- Orders events for order funnel and state aging.
- Inventory events for stock movement analytics.
- Sales events for revenue and cashier metrics.
- Billing events for receivables and payment analytics.
- Logistics events for route performance.

## Allowed dependencies

- May read from its own projection tables.
- May consume events from all domains.
- Must not call operational repositories or mutate operational tables.

## Boundary rules

1. Reporting is eventually consistent by design.
2. Reporting cannot be a dependency of command handlers.
3. A dashboard metric never decides whether an order can be approved.
4. Expensive analytics stay out of Phase 1. First prove the operational core.

## Open questions

- Which projections are required for Phase 1 operational visibility?
- Do we start with Postgres materialized views or plain projection tables?
