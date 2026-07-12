# ADR-0025: Sync journal, ownership, and conflicts

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

`PendingToSync` / `IsSynced` flags on business tables couple every aggregate to sync, weaken ordering, and block multi-destination replay. Internal outbox and cross-installation sync must stay distinct.

## Decision

### Forbidden default

Do not design Sync Worker around per-entity flags such as `PendingToSync = true` on sale/stock rows.

### Preferred pipelines

Upstream:

```text
local commit
→ domain/integration event
→ Branch Sync Journal
→ batch upstream
→ Cloud Inbox
→ idempotent apply
→ checkpoint / ack
```

Downstream:

```text
Cloud Change Feed / Sync Journal
→ Branch Inbox
→ idempotent apply
→ checkpoint
```

### Four stores (conceptual)

| Store          | Role                                                   |
| -------------- | ------------------------------------------------------ |
| Runtime Outbox | In-process integration events inside one runtime       |
| Sync Journal   | Durable cross-installation sync records (Branch↔Cloud) |
| Sync Inbox     | Received batches awaiting idempotent apply             |
| Checkpoint     | Per peer/stream high-water mark                        |

They may share infrastructure patterns. They must not share one table/semantics by default.

### Ownership

See ADR-0018 matrix. Each synced aggregate declares an owner. No generic LWW default for stock or money.

### Example A - Upstream sale

```text
Branch confirms Sale
→ journal entry (saleId, version, tenantId, branchId, branchInstanceId)
→ batch to Cloud
→ Cloud Inbox
→ idempotent apply by saleId/commandId
→ ack
→ Branch checkpoint advances
```

| Field       | Value                                                                                     |
| ----------- | ----------------------------------------------------------------------------------------- |
| Owner       | Branch                                                                                    |
| ID          | `SaleId` UUIDv7 minted on Branch                                                          |
| Version     | Monotonic aggregate version on Branch                                                     |
| Idempotency | `commandId` / journal entry id                                                            |
| Order       | Journal sequence per BranchInstance stream                                                |
| Conflict    | Cloud already has same saleId → ack duplicate; different payload same id → poison + admin |
| Retry       | Batch retry with backoff; no double apply                                                 |
| Terminal    | Applied + acked, or dead-letter                                                           |

### Example B - Downstream catalog

```text
Cloud publishes CatalogVersion
→ downstream stream
→ Branch downloads pages
→ apply by version
→ checkpoint
```

| Field       | Value                                                                                  |
| ----------- | -------------------------------------------------------------------------------------- |
| Owner       | Cloud                                                                                  |
| ID          | `CatalogVersionId` + product ids from Cloud                                            |
| Version     | Catalog version number                                                                 |
| Idempotency | version + page token                                                                   |
| Order       | Apply versions in order; skip older                                                    |
| Conflict    | Local price override policy fields only if explicitly modeled; else Cloud wins catalog |
| Retry       | Resume pages from checkpoint                                                           |
| Terminal    | Branch catalog version == published version                                            |

### Example C - E-commerce order

```text
Cloud creates order
→ downstream to Branch
→ Branch accepts/rejects
→ local fulfillment ops
→ status upstream via journal
```

| Field       | Value                                                                                          |
| ----------- | ---------------------------------------------------------------------------------------------- |
| Owner       | Cloud for create; Branch for accepted operational state                                        |
| ID          | `OrderId` from Cloud preserved                                                                 |
| Version     | Cloud order version then Branch ops version                                                    |
| Idempotency | downstream delivery id; upstream status events                                                 |
| Order       | Create before accept; accept before fulfill events                                             |
| Conflict    | Dual accept/reject → Branch decision wins ops; Cloud shows conflict if second decision arrives |
| Retry       | Standard inbox/journal                                                                         |
| Terminal    | Cancelled, Rejected, or Completed with upstream ack                                            |

Timestamps alone do not resolve stock or money conflicts.

## Consequences

### Positive

- Replay, ordering, tombstones, and multi-destination become tractable.
- Clear ownership reduces silent corruption.

### Negative / Trade-offs

- More moving parts than row flags.
- Requires careful journal schema in a later PR.

## Alternatives considered

1. **PendingToSync on every table** - Rejected.
2. **Reuse runtime Outbox as the only sync log without a journal model** - Rejected as default conflation.
3. **LWW everywhere** - Rejected.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
