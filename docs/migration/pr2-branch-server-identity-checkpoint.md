# CHECKPOINT PR 2 — BRANCH SERVER IDENTITY AND HEALTH

**Status:** Implementation complete (uncommitted on `feat/branch-server-identity`)  
**Date:** 2026-07-15  
**Base:** `ebaa9a6` (PR 1 merged)  
**Scope:** Local Branch Server installation identity + `/health/branch`. No Cloud activation, pairing, sync, Tauri, mDNS, certificates, or installer.

---

## Final model

```text
branch_instances
- id uuid PK                  = BranchInstanceId (UUIDv7, locally minted)
- singleton_key varchar(16)   = 'local'  UNIQUE + CHECK (= 'local')
- status varchar(64)          = 'ReadyForActivation' CHECK
- created_at_utc timestamptz
- xmin                        concurrency (system)
```

### Included

| Field        | Reason                               |
| ------------ | ------------------------------------ |
| Id           | Stable installation identity         |
| SingletonKey | One row per local database           |
| Status       | Current operational state            |
| CreatedAtUtc | Mint audit timestamp                 |
| xmin         | Concurrency token for future writers |

### Excluded (no producer in PR 2)

`DisplayName`, `ActivatedAtUtc`, `LastStartedAtUtc`, `TenantId`, `BranchId`, secrets, codes, hostnames, DeviceId.

---

## BranchInstanceId — local mint, Cloud-adopted

```text
first Branch boot → local UUIDv7 → persisted
future activation → Cloud adopts the same ID (no silent remint)
conflict / existing Active → reject or Replace (ADR-0017)
```

Notes recorded on ADR-0017 / 0018 / 0019. ADRs remain `Proposed`.

---

## Persisted status

Only `ReadyForActivation`. Future statuses (`Active`, …) are deferred to activation plus a migration that widens the CHECK.

---

## Singleton

`UNIQUE` index name used for race classification:

```text
ix_branch_instances_singleton_key
```

Also: `CHECK(singleton_key = 'local')` (`ck_branch_instances_singleton_key_local`) and `CHECK(status = 'ReadyForActivation')`.

Only a unique violation whose `PostgresException.ConstraintName` equals `ix_branch_instances_singleton_key` is treated as the expected concurrent race. Check / PK / connection errors propagate unchanged.

## Ensure algorithm (no UPDATE)

```text
SELECT local → if row: Publish memory + return
else INSERT UUIDv7
on unique violation of ix_branch_instances_singleton_key → Clear tracker → SELECT winner → Publish
any other DB error → propagate (no Publish)
```

Failed ensure does not publish a partial identity; the host never reaches `RunAsync`.  
`EnsureBranchRuntimeInitializedAsync` opens its own DI scope: the singleton accessor stores only `BranchInstanceInfo`, never a `DbContext`.

## Startup

| Host    | Order                                                                                      |
| ------- | ------------------------------------------------------------------------------------------ |
| Api     | Build → map endpoints → optional seed → `EnsureBranchRuntimeInitializedAsync` → `RunAsync` |
| Workers | Build → `InitializeAsync` (map + ensure) → `RunAsync`                                      |

Cloud: initializer not registered → ensure is a no-op. Branch DB failure fails startup (not fire-and-forget).

## Accessor / DTO

`IBranchInstanceAccessor` → `BranchInstanceInfo(Id, Status)` from post-ensure memory. The EF entity does not leave Platform.

## Health

```json
{ "status": "ReadyForActivation", "branchInstanceId": "..." }
```

|                  | Cloud  | Branch                 |
| ---------------- | ------ | ---------------------- |
| `/health/branch` | 404    | 200 (memory, no query) |
| OpenAPI          | absent | absent                 |

## Readiness

| Signal                         | Endpoint                          |
| ------------------------------ | --------------------------------- |
| Liveness                       | `/health`, `/health/live`         |
| Technical readiness            | `/health/ready` (DB + migrations) |
| Installation operational state | `/health/branch`                  |

`ReadyForActivation` does **not** fail `/health/ready`.

## Isolation

Table lives in the shared EF schema; producer is Branch-only. Cloud: 0 rows after startup; no accessor/initializer registered.

## Migration / SQL

- `20260715110030_Platform_BranchInstance`
- `apps/backend/db/binexus-idempotent.sql` regenerated
- `has-pending-model-changes`: none

## Tests

| Suite         | Result                  |
| ------------- | ----------------------- |
| Unit          | 80 (includes Branching) |
| Architecture  | 41                      |
| Workers.Tests | 6                       |
| Integration   | 168                     |
| Skipped       | 0                       |

## OpenAPI / SDK

No contractual change (`/health/branch` excluded; artifact restored).

## Restore / recovery

A PG restore preserves `BranchInstanceId`. Dual Principal is deferred to Replace. No MAC/hostname fingerprint.

## Verification

| Check                          | Result                                                                           |
| ------------------------------ | -------------------------------------------------------------------------------- |
| Restore/build/test Release     | green                                                                            |
| NuGet vulnerable High/Critical | 0 / 0                                                                            |
| Warnings TreatAsErrors         | 0                                                                                |
| Cloud compose smoke            | PASS — `/health/runtime=Cloud`, `/health/branch=404`, `branch_instances` count=0 |

## Risks

- Dual host with the same IDs after restore (future Replace).
- Concurrent ensure on Api + Workers (covered by PG tests).
- Empty table in the Cloud schema (documented).

---

## Proposed commits

1. `feat(backend): add BranchInstance local identity and health`
2. `test(backend): cover BranchInstance ensure and isolation`
3. `docs(migration): document Branch Server identity checkpoint`
