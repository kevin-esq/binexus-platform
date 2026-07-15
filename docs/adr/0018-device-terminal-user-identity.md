# ADR-0018: Device, Terminal, and User identity

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

POS sessions, pairing, and audit need distinct machine, role, and human identities. Collapsing them into one id breaks revocation and SalesSession invariants.

## Decision

| Concept                 | Meaning                                                 | Authority                                                       |
| ----------------------- | ------------------------------------------------------- | --------------------------------------------------------------- |
| Sucursal (`BranchId`)   | Business location                                       | Cloud creates                                                   |
| `BranchInstanceId`      | Active installed Branch Server + its Postgres           | Branch mints UUIDv7 locally; Cloud adopts on activation         |
| Device (`DeviceId`)     | Machine identity for Branch Server host or Tauri client | Created at activation or pairing                                |
| Terminal (`TerminalId`) | Logical POS/workstation role (Caja 1, Oficina)          | Branch policy assigns to a Device                               |
| User (`UserId`)         | Human actor                                             | Cloud provisions; Branch authenticates locally from synced data |

### ID minting (UUIDv7)

All new domain and identity IDs use UUIDv7 (or the platform equivalent already used in .NET modules).

Policy:

```text
ID created on Branch → kept unchanged in Cloud
ID created on Cloud → kept unchanged on Branch
```

No ID mapping tables unless an external integration requires them.

### Ownership for synced types

| Data type                            | Authority           | Primary direction |
| ------------------------------------ | ------------------- | ----------------- |
| In-person sale                       | Branch              | Branch → Cloud    |
| Cash session                         | Branch              | Branch → Cloud    |
| Physical stock movement              | Branch              | Branch → Cloud    |
| Local delivery route / stop progress | Branch              | Branch → Cloud    |
| Entitlement                          | Cloud               | Cloud → Branch    |
| Activation / BranchInstance          | Cloud               | Cloud → Branch    |
| Published catalog                    | Cloud               | Cloud → Branch    |
| Global / tenant configuration        | Cloud               | Cloud → Branch    |
| E-commerce order (initial create)    | Cloud               | Cloud → Branch    |
| Accepted order operational state     | Branch after accept | Branch → Cloud    |

Every future synced aggregate must declare an owner. Generic last-write-wins is not a default.

### SalesSession

`(TenantId, BranchId, TerminalId)` remains the open-session invariant. Device is audited separately.

## Consequences

### Positive

- Revoke a device without deleting the terminal role.
- Stable IDs across sync without remapping.

### Negative / Trade-offs

- Operators must understand Device vs Terminal during pairing.
- Ownership mistakes become sync bugs; matrix must stay current.

## Alternatives considered

1. **Device == Terminal** - Rejected.
2. **Cloud remints Branch IDs on ingest** - Rejected.
3. **Last-write-wins for all aggregates** - Rejected.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
