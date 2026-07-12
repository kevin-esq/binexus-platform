# ADR-0003: Offline-first by design

| Field    | Value                                         |
| -------- | --------------------------------------------- |
| Status   | Accepted                                      |
| Date     | 2026-05-23                                    |
| Deciders | Kevin Esquivel                                |
| Tags     | architecture, offline, sync, contracts, latam |

Note: Proposed [ADR-0024](0024-offline-first-internet-vs-lan.md) deepens this decision for Branch Runtime (internet outage vs Branch Server LAN outage). When ADR-0024 is Accepted, treat this ADR as amended by ADR-0024.

## Context and problem statement

Binexus targets real LATAM operations: corner stores, regional distributors, restaurants, delivery routes. In these environments:

- Connectivity is intermittent. A POS that requires the cloud will literally stop selling.
- Branches often have a local server (a "hub") that talks to printers, scales, scanners, and drawers.
- Truck drivers and route sellers are offline for hours.

A cloud-first SaaS pretending the internet is always there will fail in the first week of real operation. Worse, retrofitting offline support onto a system designed for synchronous request/response usually requires a rewrite.

**Question:** how do we bake offline tolerance into the foundation _now_, before we have any feature that uses it?

## Decision drivers

- **Operational continuity** — a POS that can't sell because the API is unreachable is unusable.
- **No rewrites later** — every contract we ship today must allow offline-then-sync as a future capability.
- **Phase 0 does NOT need sync code** — we just need to lock in the _contracts_ that make sync possible.
- **One artifact** — the same backend runs in the cloud and on a local hub.

## Considered options

1. **Cloud-only** — assume always-on connectivity. Add offline later.
2. **Local-first per device** — every client carries its own DB; CRDT or similar reconciliation.
3. **Local hub + cloud sync** — each branch runs a local backend; periodic sync pushes events to the cloud.
4. **Hybrid: cloud as source of truth, edge cache** — service workers / local cache, no real writes offline.

## Decision outcome

**Chosen option:** _Local hub + cloud sync, with Phase 0 baking in only the contracts_. We ship cloud-only behavior today, but every contract is designed so a future local hub can produce the exact same envelopes and reconcile with the cloud.

The concrete contracts we lock in **now**:

- Every event has a stable `id` (ULID), `correlationId`, `causationId`, and `version` (see [`packages/events/src/envelope.ts`](../../packages/events/src/envelope.ts)).
- Every command is **idempotent by `commandId`** — same `commandId` ⇒ same outcome.
- Every write is **branch-scoped** (`branchId` on the relevant rows) so two branches can independently emit events that merge without conflict.
- The **Outbox pattern** (ADR-0004) is the only way to publish events. Same recipe will work offline → sync later.
- The backend monolith is **a single artifact** — no cloud-only modules.

### Positive consequences

- Future "local hub" milestone is additive, not destructive.
- Event envelopes are already replay-safe.
- Idempotent commands trivially survive sync retries and network blips.

### Negative consequences

- Some Phase 0 work (ULID generation, `correlationId` plumbing, idempotency tokens) feels over-engineered today.
- Engineers must consciously enforce "no cloud-only assumption" in code review.

### Trade-offs accepted

- We carry the cost of designing for distribution before we distribute.
- Some optimizations (e.g. "just use a sequence id") are off the table — we use ULIDs everywhere.

## Pros and cons of the options

### Option 1 — Cloud-only

- **Good:** Simplest possible model. Fastest to ship Phase 0.
- **Bad:** Every offline feature later is a rewrite.
- **Bad:** Disqualifies the product for ~60% of the target market.

### Option 2 — Local-first per device (CRDT)

- **Good:** Maximum resilience; every client is autonomous.
- **Bad:** CRDT modeling is brutal for relational, transactional domains (stock, money).
- **Bad:** Reconciliation of conflicting writes ("did we sell the last unit twice?") is a known-hard problem.
- **Bad:** Massive Phase 0 investment for no near-term benefit.

### Option 3 — Local hub + cloud sync _(chosen, Phase N)_

- **Good:** A single point of authority per branch — much easier reasoning than per-device CRDT.
- **Good:** Single deployable artifact serves both topologies.
- **Good:** Aligns perfectly with how the target businesses actually operate (one PC per branch).
- **Bad:** Sync layer is non-trivial when we get there — but we know the shape (push outbox events upstream, pull tenant-wide reference data downstream).

### Option 4 — Edge cache only

- **Good:** Minimal change to a cloud-first design.
- **Bad:** No real offline writes — a sale during connectivity loss is _lost_.
- **Bad:** Wrong answer for the actual use cases (warehouse, route, POS).

## Validation

This decision is working if:

- Every command we ship accepts a client-provided `commandId` (or generates one client-side) and is idempotent under retry.
- Every domain event has `id`, `correlationId`, `version` — no exceptions.
- Phase N can introduce a local hub without changing existing controllers or handlers.

It is failing if:

- We catch ourselves writing `Date.now()` as a primary key (non-portable across hubs).
- A handler reads from one row and writes to another without a `commandId` short-circuit (non-idempotent).
- We start hardcoding cloud-only assumptions (e.g. "all writes go through `api.binexus.com`").

## More information

- [Local-first software (Ink & Switch)](https://www.inkandswitch.com/local-first/)
- [Designing Data-Intensive Applications](https://dataintensive.net/) — chapters on idempotency and conflict resolution.
- Related: ADR-0004 (outbox), ADR-0007 (command bus)
- Related docs: [`docs/architecture/overview.md`](../architecture/overview.md)
