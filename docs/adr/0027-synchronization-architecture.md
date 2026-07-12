# ADR-0027: Branch and Cloud synchronization architecture

| Field    | Value                                                   |
| -------- | ------------------------------------------------------- |
| Status   | Proposed                                                |
| Date     | 2026-07-12                                              |
| Deciders | Kevin Esquivel                                          |
| Tags     | sync, branch, cloud, outbox, idempotency, observability |

## Context and problem statement

Branch Runtime creates two operational surfaces. Branch owns in-person work for a sucursal, and Cloud owns tenant administration, cross-branch reporting, and coordination. The two surfaces need asynchronous synchronization that preserves local sales continuity and avoids conflict-heavy merges for stock and money.

ADR-0004 already chose the Outbox pattern for reliable event publication. Branch Runtime uses that pattern as the upstream source of operational facts. Downstream sync moves Cloud-owned reference data and policy to Branch. This ADR describes the architecture only; it does not describe code to write now.

**Question:** how should Branch and Cloud exchange operational facts and configuration without placing sync inside the POS request pipeline?

## Decision drivers

- **No sale-path sync** - `CreateSale` and other in-person commands must not wait for Cloud.
- **Reliable upstream facts** - Branch must ship committed sales, stock movements, and operational events without losing them.
- **Controlled downstream data** - Cloud must deliver catalog, prices, users, roles, feature flags, and tenant config to Branch.
- **Idempotency** - Retries must not duplicate money, stock, or events.
- **Prioritization** - Security and pricing changes may outrank historical reporting.
- **Operability** - Sync needs checkpoints, batching, backoff, compression, and observability.
- **Conflict avoidance** - Stock and money need explicit authority, not bidirectional CRDT merge.

## Considered options

1. **Branch-side Sync Worker with Cloud ingest and projection endpoints** - Branch pushes upstream facts and pulls downstream changes asynchronously.
2. **Sync inside request pipeline** - Operational commands call Cloud before returning.
3. **Bidirectional CRDT merge for stock and money** - Branch and Cloud both accept writes and merge conflicts later.
4. **Database replication between Branch and Cloud** - PostgreSQL replication or dump shipping moves data across environments.

## Decision outcome

**Chosen option:** _Branch-side Sync Worker with Cloud ingest and projection endpoints_, because it preserves Branch authority while giving Cloud a reliable, observable integration surface.

The Sync Worker runs on the Branch side. It reads branch outbox records and local checkpoints, batches upstream facts, sends them to Cloud ingest endpoints, and advances checkpoints only after Cloud acknowledges durable receipt. Cloud exposes ingest endpoints for upstream facts and projection endpoints for downstream changes.

### Upstream: Branch to Cloud

Branch sends committed operational facts upstream:

- Outbox events from branch domain modules.
- Sales and payment facts.
- Stock movements and inventory adjustments.
- Sales session and cash reconciliation facts.
- Warehouse and logistics operational facts.
- Device, branch health, and sync telemetry as needed.

Each upstream item carries stable identifiers such as `eventId`, `commandId`, `tenantId`, `branchId`, `branchInstanceId`, and version metadata. Cloud treats those identifiers as idempotency keys.

### Downstream: Cloud to Branch

Cloud sends branch-relevant reference and policy data downstream:

- Catalog and product data.
- Price lists and promotions.
- Users, roles, and branch assignments.
- Feature flags.
- Tenant and branch configuration.
- Device revocations and credential policy.

Downstream changes carry sequence or version metadata so Branch can apply them once, in order per stream where ordering matters.

### Sync mechanics

| Concern       | Decision                                                                                                                                           |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Checkpoints   | Branch stores upstream and downstream checkpoints per stream. Cloud stores received high-water marks per branch instance.                          |
| Batching      | Sync batches by stream, size, time, and priority. Large batches split without changing item identity.                                              |
| Idempotency   | `eventId` dedupes events; `commandId` dedupes command effects where commands cross boundaries; downstream items carry stable ids and versions.     |
| Priorities    | Security revocations, user and role changes, prices, and feature flags outrank historical reporting batches.                                       |
| Backoff       | Branch retries transient failures with bounded exponential backoff and jitter. Permanent validation failures enter a dead-letter or support queue. |
| Compression   | Branch may compress large batches after handshake negotiates support.                                                                              |
| Observability | Branch and Cloud record lag, batch size, retry count, last success, failed item ids, and checkpoint position.                                      |

### Cloud responsibilities

Cloud does not participate in an in-person sale. Cloud ingests branch facts, dedupes them, stores raw envelopes or normalized facts as needed, and projects them into reporting, administration, and cross-branch views. Cloud owns downstream streams for reference data and policy.

### Positive consequences

- Branch commands stay fast and available during Cloud outages.
- The outbox becomes the upstream sync source without changing command handlers.
- Cloud can dedupe retries by stable identifiers.
- Operators and support can inspect sync lag and failed items.
- Downstream config changes have a clear path to Branch.

### Negative consequences

- Cloud views lag behind Branch until sync catches up.
- Sync Worker becomes a required component of Principal Server operations.
- Poison messages and schema evolution need explicit handling.
- Downstream ordering rules add complexity to Cloud projections.

### Trade-offs accepted

- Binexus accepts eventual Cloud consistency for branch facts.
- Binexus rejects bidirectional CRDT conflict merge for stock and money.
- Binexus keeps sync outside request handlers, even for commands that produce high-value sales facts.
- Binexus accepts branch-side worker operations instead of direct database replication.

## Pros and cons of the options

### Option 1 - Branch-side Sync Worker with Cloud ingest and projection endpoints

- **Good:** Keeps sale critical path local.
- **Good:** Uses outbox records as the reliable upstream source.
- **Good:** Supports retries, batching, compression, and observability.
- **Good:** Makes upstream and downstream ownership explicit.
- **Bad:** Adds a worker, checkpoints, and Cloud ingest surface.
- **Bad:** Requires careful schema evolution across versions.

### Option 2 - Sync inside request pipeline

- **Good:** Cloud receives important facts before the request returns.
- **Bad:** Cloud outage or latency slows or blocks sales.
- **Bad:** Couples local command success to network health.
- **Bad:** Violates ADR-0026 for `CreateSale`.

### Option 3 - Bidirectional CRDT merge for stock and money

- **Good:** Both sides can accept writes independently.
- **Bad:** Stock and money conflicts need business resolution, not generic merge rules.
- **Bad:** Oversell and cash variance become normal outcomes.
- **Bad:** Complexity exceeds Binexus needs when Branch already owns in-person authority.

### Option 4 - Database replication between Branch and Cloud

- **Good:** Mature database tooling exists for some replication paths.
- **Bad:** Replicates storage shape instead of domain facts.
- **Bad:** Hard to enforce tenant, branch, version, and idempotency contracts at the boundary.
- **Bad:** Poor fit for intermittent customer LAN connectivity and evolving module schemas.

## Validation

This decision is working if:

- `CreateSale` commits locally and returns without Cloud ingest.
- Branch outbox records flow upstream through the Sync Worker with idempotent Cloud ingest.
- Cloud can receive the same batch twice without duplicating sales, payments, or stock movements.
- Branch pulls downstream user, price, catalog, feature flag, and tenant config changes by checkpoint.
- Operators can see sync lag, last success, retry counts, and failed item identifiers.

It is failing if:

- A request handler performs sync work before returning a sale result.
- Cloud and Branch both write stock or money facts and rely on CRDT merge.
- A retry duplicates a sale, payment, stock movement, or outbox event.
- Support cannot tell whether a branch is caught up.

## More information

- Related ADRs: [ADR-0003](0003-offline-first-design.md), [ADR-0004](0004-event-driven-with-outbox-pattern.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0023](0023-branch-installation.md), [ADR-0026](0026-offline-first-strategy.md)
