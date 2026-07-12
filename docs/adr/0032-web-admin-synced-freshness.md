# ADR-0032: Web Admin and synced data freshness

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Web Admin must not become a remote desktop into the branch LAN. Operators will assume live stock when Branch is offline unless the UI shows freshness.

## Decision

Web Admin talks **only** to Cloud Runtime.

### Web Admin may

- Administer configuration
- Publish catalogs and prices
- View consolidated information
- View sync status
- Issue activations / replace tokens
- Administer devices and sucursales (Cloud records)

### Web Admin must not

- Connect to the branch LAN IP
- Write Branch PostgreSQL directly
- Assume real-time branch truth while disconnected

### Freshness fields on operational cloud views

| Field               | Meaning                                         |
| ------------------- | ----------------------------------------------- |
| `LastSyncedAt`      | Last successful sync for that stream/entity set |
| `SyncStatus`        | Healthy / Delayed / Disconnected / Error        |
| `DataFreshness`     | Fresh / Stale / Unknown                         |
| `PendingOperations` | Approximate when known from Cloud-side queues   |

### Existing web operator screens

Do not delete current web POS/ops screens in the architecture phase. Destination:

| Surface                        | Destination                                                  |
| ------------------------------ | ------------------------------------------------------------ |
| Cloud admin / config / publish | Remain on Web Admin → Cloud                                  |
| In-person POS / caja           | Move primary UX to Branch Client (Tauri) over time           |
| Live branch-only ops           | Prefer Tauri; web may show synced read models with freshness |

## Consequences

### Positive

- Honest cloud UX during outages.
- Clear network boundary.

### Negative / Trade-offs

- Dual UI period during migration to Tauri.
- Extra API fields for freshness.

## Alternatives considered

1. **Web Admin VPN into Branch API** - Rejected for v1 product shape.
2. **Hide stale data without labels** - Rejected.
3. **Delete web ops screens immediately** - Rejected this phase.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
