# Web vs Desktop surfaces

`Web vs Desktop surfaces` defines where operator and administration workflows should live during the Branch Runtime migration.

Related docs:

- [Architecture overview](./overview.md)
- [Branch Runtime architecture](./branch-runtime.md)
- [Desktop Tauri architecture](./desktop-tauri.md)
- [ADR-0016: Runtime modes for Cloud and Branch](../adr/0016-runtime-modes-cloud-vs-branch.md)
- [ADR-0024: Branch local HTTP API](../adr/0024-local-http-api.md)
- [ADR-0026: Offline-first strategy for Branch Runtime](../adr/0026-offline-first-strategy.md)

## Migration principle

Desktop becomes the operations authority UI at the branch. Web becomes the admin and cloud supervision UI.

No current web operator screen should be deleted during the first migration slice. Each surface moves after Desktop reaches role coverage, hardware coverage, API parity, and rollout controls.

## Surface split

| Surface                      | Moves to Desktop                          | Stays on Web                                        | Migration note                                                 |
| ---------------------------- | ----------------------------------------- | --------------------------------------------------- | -------------------------------------------------------------- |
| POS sales                    | Yes                                       | Synced summaries and supervision                    | Desktop needs branch continuity, printers, drawers, and scales |
| Sales sessions               | Yes                                       | Audit and cross-branch reporting                    | Cash session open and close belongs near the Caja              |
| Receipts                     | Yes                                       | Receipt templates and policy configuration          | Printing belongs to Tauri host hardware commands               |
| Returns and refunds          | Yes, when supported at branch             | Policy, approvals, and analytics                    | Branch API owns rules; Web can supervise exceptions            |
| Warehouse picking            | Yes                                       | Global monitoring and configuration                 | Branch workers need local availability during outages          |
| Packing and dispatch handoff | Yes                                       | SLA dashboards and exception review                 | Branch operators need local continuity                         |
| Inventory counts             | Yes                                       | Cross-branch variance reporting                     | Local counts must work without Cloud on the path               |
| Stock adjustments            | Yes, with permissions                     | Policy, approvals, and audit review                 | Desktop captures the operation; Web supervises controls        |
| Stock transfers              | Yes for branch execution                  | Planning, cross-branch review, and approvals        | Split execution from supervision                               |
| Orders intake                | Yes for branch-local operational intake   | E-commerce, customer service, and cloud order views | Keep cloud channels in Web                                     |
| Order fulfillment            | Yes                                       | Cross-branch status and exception queues            | Branch handles execution; Web tracks network-wide state        |
| Logistics route preparation  | Yes                                       | Fleet supervision and historical analytics          | Branch prepares local routes and proof packets                 |
| Delivery proof review        | Yes for branch capture and local review   | Cloud audit and customer service views              | Proof files sync upstream after local capture                  |
| Route liquidation            | Yes                                       | Finance reporting and exception oversight           | Local branch closes the route; Web audits across branches      |
| Branch diagnostics           | Yes for local service and hardware checks | Fleet-wide health and alerts                        | Principal diagnostics need local Windows visibility            |
| Device pairing               | Yes for device setup                      | Pairing code generation and device administration   | Cloud authorizes; Desktop completes local pairing              |
| User login                   | Yes                                       | User administration                                 | Desktop authenticates users for branch work                    |
| User management              | No                                        | Yes                                                 | Cloud remains the source for users, roles, and policy          |
| Tenant administration        | No                                        | Yes                                                 | Web owns tenant-level administration                           |
| Branch administration        | Partial local diagnostics only            | Yes                                                 | Web owns branch creation, settings, and lifecycle              |
| Catalog management           | No, except local read views               | Yes                                                 | Product and price authority stays in Cloud                     |
| Price lists                  | No, except local read views               | Yes                                                 | Sync distributes prices to Branch Runtime                      |
| Subscription and billing     | No                                        | Yes                                                 | Cloud owns account and commercial state                        |
| Analytics                    | No, except local operational dashboards   | Yes                                                 | Web owns historical and cross-branch reporting                 |
| Supervision                  | No, except local alerts                   | Yes                                                 | Web owns multi-branch oversight                                |
| Feature flags                | No                                        | Yes                                                 | Cloud controls rollout and policy                              |
| Integrations                 | No                                        | Yes                                                 | Cloud owns third-party setup and credentials                   |
| E-commerce                   | No                                        | Yes                                                 | Customer-facing and channel surfaces stay in Web               |

## Desktop authority criteria

A surface can move to Desktop when it satisfies these conditions:

| Criterion         | Requirement                                                            |
| ----------------- | ---------------------------------------------------------------------- |
| Branch API parity | Desktop can complete the workflow through Branch API endpoints         |
| Offline tolerance | The workflow does not require Cloud on the operational path            |
| Device identity   | Commands carry paired device identity where needed                     |
| User identity     | Commands carry authenticated user identity and role grants             |
| Hardware coverage | Required printers, scales, drawers, or scanners work through Tauri IPC |
| Outbox coverage   | Durable events exist for sync and audit                                |
| Web visibility    | Web can show synced outcomes after upstream sync                       |
| Rollout control   | Feature flags or tenant settings can stage the migration               |

## Web retention criteria

A surface should stay on Web when it depends on cloud authority, cross-branch supervision, or account-level controls.

| Criterion              | Example                                          |
| ---------------------- | ------------------------------------------------ |
| Tenant-wide authority  | User roles, feature flags, tenant settings       |
| Commercial authority   | Subscription, invoices, plan limits              |
| Cross-branch analytics | Sales trends, stock variance, route KPIs         |
| Channel ownership      | E-commerce, integrations, customer service       |
| Policy administration  | Approval rules, refund policies, branch settings |
| Historical reporting   | Long-range reporting and exports                 |

## Migration guardrails

| Guardrail             | Rule                                                                                        |
| --------------------- | ------------------------------------------------------------------------------------------- |
| No deletion first     | Keep existing web operator screens until Desktop reaches parity and rollout validates usage |
| One surface at a time | Move POS, warehouse, logistics, inventory, and orders through separate slices               |
| Branch API first      | Desktop must not call Cloud directly for branch operations in Branch mode                   |
| Shared contracts      | Generated types and event envelopes should stay shared where possible                       |
| Synced visibility     | Web must still show branch outcomes after sync                                              |
| Clear labels          | UI should distinguish local branch operation from cloud supervision                         |

The migration succeeds when branch staff can run daily operations from Desktop and managers can supervise the business from Web.
