# ADR-0028: Branch Runtime conflict resolution

| Field    | Value                                             |
| -------- | ------------------------------------------------- |
| Status   | Proposed                                          |
| Date     | 2026-07-12                                        |
| Deciders | Kevin Esquivel                                    |
| Tags     | branch, sync, conflict-resolution, stock, pricing |

## Context and problem statement

Branch Runtime lets a sucursal keep selling, moving stock, and running local operations when Cloud connectivity fails. ADR-0026 gives Branch authority for in-person work. ADR-0027 moves Branch facts upstream and Cloud-owned reference data downstream through asynchronous sync.

That topology creates conflict questions when Cloud and Branch both know about stock, money, catalog, prices, users, and configuration. Stock and money represent business invariants, so Binexus cannot merge those records with generic CRDT rules or silent arithmetic. Reference data can tolerate narrower rules when one side owns the field.

**Question:** how should Binexus resolve sync conflicts without duplicating sales, overselling stock, or hiding operator intervention?

## Decision drivers

- **Stock and money integrity** - Conflict handling must protect cash, payments, stock movements, and reservations.
- **Branch operational authority** - Branch owns operational facts created offline at that branch.
- **Cloud policy authority** - Cloud owns downstream catalog, prices, tenant policy, and cross-branch administration.
- **Idempotency** - Retries must not duplicate sales, payments, stock movements, or events.
- **Admin visibility** - Binexus must surface unresolved conflicts instead of silently changing business facts.
- **Limited safe merging** - Last-writer-wins may apply only to non-invariant reference fields.

## Considered options

1. **Explicit authority with conflict surfacing** - Branch owns offline operational facts, Cloud owns downstream policy and reference data, and Cloud rejects impossible downstream states for admin review.
2. **Automatic CRDT merge for all replicated data** - Branch and Cloud accept concurrent writes and merge them generically.
3. **Cloud always wins** - Cloud overwrites Branch data during sync conflicts.
4. **Branch always wins** - Branch overwrites Cloud data during sync conflicts.

## Decision outcome

**Chosen option:** _Explicit authority with conflict surfacing_, because stock and money need named authority and admin review when facts cannot produce a valid downstream state.

Branch is the authority for operational facts created offline at that branch. Those facts include sales, payments, sales session facts, stock movements, warehouse actions, logistics actions, and related outbox events. Branch emits stable `commandId` and `eventId` values, and Cloud uses them as idempotency keys.

Cloud owns downstream catalog and price state. When Branch receives catalog or price updates, Cloud wins for the downstream stream. If a local operation used an older price while offline, Branch keeps the sale fact as recorded and Cloud treats the sale as a historical branch fact rather than rewriting it to the new price.

Cloud rejects impossible downstream states. Examples include a stock movement that would create an invalid projection, a duplicate payment event with conflicting values, or an event sequence that violates an aggregate invariant. Cloud records the failed item, keeps checkpoints from advancing past the conflict where ordering requires it, and exposes the conflict to the admin UI or support queue.

Last-writer-wins applies only to non-invariant reference fields where Binexus can prove the field has no stock, money, identity, authorization, or compliance effect. A product display name may use last-writer-wins when Cloud owns the catalog stream and the version is newer. Stock quantity, payment amount, tax amount, cash session balance, and reservation state never use last-writer-wins.

### Conflict handling table

| Data category                                                             | Authority                    | Conflict rule                                                                                                               |
| ------------------------------------------------------------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Branch sales, payments, stock movements, warehouse facts, logistics facts | Branch that created the fact | Cloud ingests idempotently by `commandId` or `eventId`; impossible projections become admin-visible conflicts.              |
| Catalog and prices                                                        | Cloud                        | Branch applies downstream versions from Cloud; Branch does not rewrite existing sale facts.                                 |
| Users, roles, feature flags, tenant and branch config                     | Cloud                        | Branch applies downstream versions by stream and checkpoint; local operation follows the last applied policy while offline. |
| Non-invariant display fields                                              | Owning stream                | Last-writer-wins only when the field has no stock, money, authorization, or compliance effect.                              |

### Positive consequences

- Branch can keep operating offline without turning sync into a silent merge system.
- Cloud can dedupe retries by `commandId` and `eventId`.
- Catalog and price authority remains clear for downstream updates.
- Administrators can see conflicts that need business judgment.
- Stock and money stay under explicit invariant checks.

### Negative consequences

- Cloud needs conflict records, dead-letter handling, and admin UI exposure.
- Support teams need procedures for resolving impossible downstream states.
- Some Cloud reports can pause or show partial data until an operator resolves a conflict.
- Developers must classify fields before using last-writer-wins.

### Trade-offs accepted

- Binexus accepts manual review for invariant conflicts instead of silent stock or money merge.
- Binexus accepts that Cloud reports can lag when a conflict blocks a projection.
- Binexus accepts narrower last-writer-wins rules to keep field ownership explicit.

## Pros and cons of the options

### Option 1 - Explicit authority with conflict surfacing

- **Good:** Protects stock and money invariants.
- **Good:** Matches ADR-0026 Branch authority and ADR-0027 sync architecture.
- **Good:** Keeps catalog and price downstream ownership with Cloud.
- **Good:** Gives support and admins a named place to resolve impossible states.
- **Bad:** Requires admin UI and support workflow for conflicts.
- **Bad:** Requires field-level ownership classification.

### Option 2 - Automatic CRDT merge for all replicated data

- **Good:** Reduces manual intervention for simple fields.
- **Bad:** Generic merge rules cannot decide whether two sales consumed the same final unit.
- **Bad:** Cash and payment conflicts need business judgment.
- **Bad:** Silent merge can hide oversell and reconciliation errors.

### Option 3 - Cloud always wins

- **Good:** Cloud projections stay simple.
- **Bad:** Cloud can erase valid offline branch sales and stock movements.
- **Bad:** Violates Branch authority for in-person work.
- **Bad:** Makes Cloud connectivity a hidden dependency for operational truth.

### Option 4 - Branch always wins

- **Good:** Preserves every local branch fact.
- **Bad:** Branch can ignore Cloud-owned catalog, price, user, and policy changes.
- **Bad:** Cross-branch administration loses authority.
- **Bad:** Security revocation and price updates can fail to take effect after sync.

## Validation

This decision is working if:

- Cloud receives duplicate upstream batches without duplicating sales, payments, stock movements, or events.
- Cloud rejects impossible stock or money projections and exposes them to the admin UI or support queue.
- Branch applies Cloud-owned catalog and price changes from the downstream stream.
- Last-writer-wins appears only on approved non-invariant reference fields.
- Existing sale facts keep their recorded price even when Cloud later changes the catalog price.

It is failing if:

- A retry creates two sales from the same `commandId`.
- Cloud silently merges conflicting stock quantities.
- A stock, payment, tax, or cash-session conflict uses last-writer-wins.
- A Branch overwrites Cloud-owned price data with an offline edit.

## More information

- Related ADRs: [ADR-0003](0003-offline-first-design.md), [ADR-0004](0004-event-driven-with-outbox-pattern.md), [ADR-0026](0026-offline-first-strategy.md), [ADR-0027](0027-synchronization-architecture.md)
