# ADR-0017: One authoritative BranchInstance per sucursal

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

A sucursal needs one operational authority. Two Branch Servers writing for the same `BranchId` corrupt stock, sessions, and sync. Local multi-node HA is out of scope for v1.

## Decision

Invariant:

```text
one sucursal
→ one active BranchInstance
→ one authoritative PostgreSQL database
```

### Active instance

Cloud stores at most one `BranchInstanceId` with status `Active` per `(TenantId, BranchId)`.

`BranchInstanceId` is **locally minted** (UUIDv7) on first Branch Server startup and **cloud-adopted** during activation. Cloud must not silently replace the ID; it may reject activation on conflict or an existing Active instance (Replace flow).

Branch Server persists the local `BranchInstanceId` before activation (status `ReadyForActivation` in the early implementation).

### Accidental second server

Detection (design):

1. Activation against Cloud fails if an Active instance already exists for the branch, unless the operator starts an explicit **Replace** flow.
2. LAN discovery may show multiple candidates; pairing/activation UX must show InstanceId fragment, display name, version, and fingerprint so operators do not attach to a stray host.
3. Branch Server advertises `BranchInstanceId` and activation fingerprint. A second host that reuses stolen config without Cloud Replace is rejected when sync credentials fail Cloud challenge.

Prevention:

- Activation codes are single-use and bound to `(TenantId, BranchId)`.
- Replace requires Cloud-issued replace token and marks the prior instance `Replaced` before issuing a new Active instance.
- Do not allow silent re-activation of a second Active instance for the same branch.

### Primary vs replacement

| Role             | Meaning                                                                   |
| ---------------- | ------------------------------------------------------------------------- |
| Primary (active) | Current authoritative Branch Server + Postgres for the sucursal           |
| Replacement      | New host that will become primary after Cloud Replace + restore/bootstrap |

Replacement sequence (design):

1. Operator requests Replace in Web Admin.
2. Cloud marks current instance `PendingReplace`, issues replace token.
3. New Principal installs via Branch Installer, activates with replace token.
4. Restore from backup and/or resume sync from Cloud checkpoints as documented in ADR-0030.
5. Cloud sets new instance `Active`, old instance `Replaced` (credentials revoked).

### Out of scope

- Automatic failover.
- Active-active clustering.
- Shared storage multi-node PostgreSQL.
- Automatic election between two LAN servers.

## Consequences

### Positive

- Clear authority for sales and stock.
- Explicit human Replace path for hardware failure.

### Negative / Trade-offs

- Principal downtime stops Branch Clients until restore or replace.
- Operators must follow Replace; dual live servers are a support incident.

## Alternatives considered

1. **Multiple Branch Servers with conflict merge** - Rejected for v1.
2. **Cloud as fallback write path for POS** - Rejected: breaks offline-first.
3. **Per-terminal authoritative DB** - Rejected: splits authority.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
