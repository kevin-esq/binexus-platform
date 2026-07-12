# Web vs Desktop surfaces

| Surface                      | Talks to                    | Role                                                              |
| ---------------------------- | --------------------------- | ----------------------------------------------------------------- |
| Web Admin                    | Cloud Runtime only          | SaaS admin, publish, activations, consolidated views, sync status |
| Web operator panel (current) | Cloud API today             | Interim ops; in-person POS migrates toward Tauri over time        |
| Branch Client (Tauri)        | Branch Server LAN           | In-person authoritative ops                                       |
| Branch Server                | Local Postgres + Cloud sync | Authority for sucursal                                            |

Related: [ADR-0032](../adr/0032-web-admin-synced-freshness.md), [ADR-0016](../adr/0016-three-installation-modes.md).

## Web Admin rules

May: configure, publish catalog/prices, view consolidated data, view sync health, issue activation/replace tokens, manage Cloud device/branch records.

Must not: open branch LAN IPs, write Branch Postgres, imply live truth without freshness.

Operational cloud views show `LastSyncedAt`, `SyncStatus`, `DataFreshness`, and approximate `PendingOperations` when known.

## Destination of current web screens

| Screen class         | Destination                |
| -------------------- | -------------------------- |
| Tenant/billing/admin | Stay Web Admin → Cloud     |
| Catalog publish      | Stay Web Admin → Cloud     |
| In-person POS / caja | Primary UX → Branch Client |
| Synced read-only ops | Web with freshness labels  |

Do not delete current web ops screens in the architecture phase.
