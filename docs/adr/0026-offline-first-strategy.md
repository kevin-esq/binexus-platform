# ADR-0026: Offline-first strategy for Branch Runtime

| Field    | Value                                           |
| -------- | ----------------------------------------------- |
| Status   | Proposed                                        |
| Date     | 2026-07-12                                      |
| Deciders | Kevin Esquivel                                  |
| Tags     | offline, branch, cloud, sync, pos, availability |

## Context and problem statement

ADR-0003 accepted offline-first design while Binexus still treated local hub behavior as future contracts. Branch Runtime turns that future shape into an operational strategy. The branch backend becomes the authority for in-person work, and Cloud becomes the coordination, reporting, tenant administration, and cross-branch surface.

Cloud does not participate in an in-person sale when Branch is present. A Cloud outage must not stop POS, warehouse, or logistics operations inside the sucursal. Sync moves facts and configuration asynchronously; it does not sit on the sale critical path.

**Question:** how does Binexus define offline-first once Branch Runtime exists as a real deployment target?

## Decision drivers

- **Sales continuity** - A branch must keep selling when Cloud connectivity fails.
- **Operational authority** - Branch owns in-person commands and local invariants.
- **Async sync** - Network recovery should reconcile facts after work happens.
- **One database per branch** - One PostgreSQL per sucursal avoids intra-branch stock and cash conflicts.
- **Shared modules** - Cloud and Branch share domain behavior through the .NET modular monolith.
- **Clear ADR relationship** - ADR-0003 remains Accepted until ADR-0026 is accepted.

## Considered options

1. **Branch Runtime offline-first operational strategy** - Branch continues operations locally and syncs asynchronously with Cloud.
2. **Cloud-first with Branch cache** - Branch caches reads and forwards writes to Cloud when possible.
3. **Terminal-local offline writes** - Each terminal records sales locally and merges later.
4. **Cloud-required checkout** - Branch allows browsing offline but requires Cloud for final sale commit.

## Decision outcome

**Chosen option:** _Branch Runtime offline-first operational strategy_, because it preserves sales continuity and gives each sucursal one local authority for stock, money, and operational facts.

ADR-0026 amends and deepens ADR-0003. ADR-0003 remains Accepted until ADR-0026 is accepted. Once accepted, ADR-0026 becomes the operational strategy ADR for Branch Runtime and supersedes the parts of ADR-0003 that describe offline support as Phase 0 contracts only.

Branch Runtime must support full local operation for:

- POS sales and sales sessions.
- Warehouse actions needed by in-branch operations.
- Logistics actions that branch staff perform locally.
- Inventory movements caused by those operations.
- User authentication and authorization against local Branch Identity.

Sync is asynchronous. A Branch command commits against the branch PostgreSQL database, records outbox facts, and returns based on local success or failure. The request pipeline for `CreateSale` and other in-person commands must not wait for Cloud ingest, Cloud projection, or downstream acknowledgement.

### Positive consequences

- Cloud outages do not stop in-person revenue.
- Branch owns stock and cash invariants for its sucursal.
- Operators can keep using POS, warehouse, and logistics flows during network loss.
- Sync can prioritize, retry, and observe transfer without slowing sales.
- ADR-0003 gains a concrete runtime interpretation instead of remaining a future constraint.

### Negative consequences

- Branch installs need local database, backup, monitoring, and recovery.
- Cloud reports can lag behind Branch reality.
- Cross-branch views need staleness indicators.
- Downstream changes from Cloud, such as user revocation or price updates, may arrive late during outages.

### Trade-offs accepted

- Binexus accepts eventual Cloud consistency for branch facts to preserve local sales continuity.
- Binexus accepts operational complexity at the branch to avoid Cloud availability as a revenue dependency.
- Binexus rejects terminal-local conflict merging for stock and money.

## Pros and cons of the options

### Option 1 - Branch Runtime offline-first operational strategy

- **Good:** Branch sales continue during Cloud outage.
- **Good:** One local authority owns branch stock, sessions, and cash.
- **Good:** Matches ADR-0002 modular monolith and ADR-0004 outbox.
- **Good:** Gives sync clear upstream and downstream responsibilities.
- **Bad:** Requires local infrastructure and backup discipline.
- **Bad:** Cloud can show stale operational data.

### Option 2 - Cloud-first with Branch cache

- **Good:** Cloud remains the only write authority.
- **Bad:** A connectivity loss can still block writes.
- **Bad:** Branch becomes a cache, not an authority.
- **Bad:** POS behavior depends on network conditions operators cannot control.

### Option 3 - Terminal-local offline writes

- **Good:** A cashier terminal can continue if the Principal fails.
- **Bad:** Multiple terminals can oversell the same stock.
- **Bad:** Cash session and ticket reconciliation become conflict-heavy.
- **Bad:** Violates the one PostgreSQL per sucursal authority model.

### Option 4 - Cloud-required checkout

- **Good:** Cloud sees every sale immediately.
- **Bad:** Cloud outage stops the most important operation.
- **Bad:** Creates a misleading offline UX where browsing works but selling fails.

## Validation

This decision is working if:

- A Branch can complete POS sales while Cloud is unreachable.
- `CreateSale` does not call Cloud or wait for sync acknowledgement.
- Branch warehouse and logistics actions that belong to in-person work commit locally.
- Cloud dashboards and admin screens label data staleness when sync lags.
- Sync catches up from branch outbox facts after connectivity returns.

It is failing if:

- Cloud outage stops in-person sales.
- A Branch request path waits on Cloud ingest.
- A terminal writes directly to its own local database and later merges stock or cash.
- ADR-0003 remains the only offline decision after Branch Runtime becomes real.

## More information

- Amends: [ADR-0003](0003-offline-first-design.md)
- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0004](0004-event-driven-with-outbox-pattern.md), [ADR-0023](0023-branch-installation.md), [ADR-0025](0025-local-authentication.md), [ADR-0027](0027-synchronization-architecture.md)
