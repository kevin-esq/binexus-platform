# ADR-0025: Branch local authentication

| Field    | Value                                     |
| -------- | ----------------------------------------- |
| Status   | Proposed                                  |
| Date     | 2026-07-12                                |
| Deciders | Kevin Esquivel                            |
| Tags     | branch, identity, jwt, rbac, offline, pos |

## Context and problem statement

Branch runtime must authenticate cashier, warehouse, and logistics users while Cloud connectivity is unavailable. A branch cannot stop in-person sales because Cloud identity is unreachable. At the same time, device pairing must not become a shared cashier password.

ADR-0006 chose JWT access tokens, refresh rotation, Argon2id password hashing, and RBAC. Branch runtime keeps that session model, but the Branch Identity module verifies local users against the branch PostgreSQL database and signs branch-local tokens with branch-instance-specific keys.

**Question:** how do users authenticate on a Branch runtime without making Cloud part of the POS request path?

## Decision drivers

- **Offline operation** - Branch must authenticate users without Cloud during outages.
- **User accountability** - Every sale and cash session needs a human user identity, not a device-only credential.
- **JWT continuity** - Branch should preserve the ADR-0006 token model on LAN.
- **Branch-specific signing** - A stolen key from one branch must not sign tokens for another branch.
- **Local credential cache** - Branch needs user credentials or hashes locally before an outage.
- **Future revocation** - Cloud must be able to revoke or change users through downstream sync.

## Considered options

1. **Branch Identity with local users, hashes, and branch-specific JWT keys** - Users authenticate against local PostgreSQL and receive LAN JWTs.
2. **Cloud-only authentication for POS** - Branch terminals redirect or call Cloud for every login.
3. **Shared device password without user identity** - Cashiers unlock POS with a device or branch password.
4. **Long-lived Cloud tokens cached on terminals** - Terminals reuse Cloud-issued tokens until connectivity returns.

## Decision outcome

**Chosen option:** _Branch Identity with local users, hashes, and branch-specific JWT keys_, because Branch must authorize in-person work while preserving user accountability and the ADR-0006 session model.

The Branch Identity module stores cached user records, roles, branch assignments, and password hashes in the branch PostgreSQL database. The branch signs JWT access tokens with keys generated for that `BranchInstanceId` and stored in the Principal Server OS secret store.

JWTs on LAN carry the same business claims expected by modules where possible:

- `sub` for user identity.
- `tenantId` and `branchId`.
- `role` for RBAC.
- `branchInstanceId` for local issuer context when needed.

Device pairing proves that a terminal may connect to a branch. User login proves which person operates the terminal. The two flows stay separate.

### Offline and downstream revocation

Branch works without Cloud as long as it has the relevant users and password hashes synced locally. Cloud can change or revoke users, roles, and branch assignments through downstream sync in a future accepted sync design. Branch applies those changes locally when connectivity returns. Until then, local policy and token expiry govern access.

High-risk administrative actions may require stricter local policy, but ordinary POS, warehouse, and logistics work must not depend on Cloud authentication.

### Positive consequences

- Cashiers can sign in and sell during Cloud outages.
- JWT and RBAC semantics remain consistent with ADR-0006.
- A compromised branch signing key affects one branch instance, not every tenant branch.
- User audit trails keep human accountability for sale, session, stock, and cash actions.
- Cloud remains the source of user provisioning, with Branch as local operational authority.

### Negative consequences

- User and role sync becomes security-sensitive downstream data.
- Revocation during a long outage is not immediate on the branch.
- Branch key backup and rotation need operational support.
- Password hash storage exists in every branch database that has offline users.

### Trade-offs accepted

- Binexus accepts revocation delay during Cloud outages to keep in-person sales operating.
- Binexus accepts local password hashes because offline authentication needs a verifier.
- Binexus keeps branch keys per instance instead of one tenant-wide signing key.

## Pros and cons of the options

### Option 1 - Branch Identity with local users, hashes, and branch-specific JWT keys

- **Good:** Supports login and authorization without Cloud.
- **Good:** Preserves user-level accountability.
- **Good:** Keeps JWT validation stateless on the local request path.
- **Good:** Limits signing-key blast radius to one branch instance.
- **Bad:** Requires secure local storage and downstream user sync.
- **Bad:** Revocation arrives after connectivity returns.

### Option 2 - Cloud-only authentication for POS

- **Good:** Centralized revocation and policy.
- **Bad:** Cloud outage blocks user login and can stop sales.
- **Bad:** Violates Branch operational authority for in-person work.
- **Bad:** Creates a hidden Cloud dependency in the POS path.

### Option 3 - Shared device password without user identity

- **Good:** Simple offline unlock.
- **Bad:** No per-user audit trail.
- **Bad:** Lost or shared passwords compromise the branch.
- **Bad:** Cannot enforce RBAC or cashier accountability.

### Option 4 - Long-lived Cloud tokens cached on terminals

- **Good:** Avoids local password verification.
- **Bad:** Token theft has a large window.
- **Bad:** Terminal replacement and revocation become fragile.
- **Bad:** Branch cannot onboard a synced user cleanly without Cloud token refresh.

## Validation

This decision is working if:

- A user can log in to Branch while Cloud is unreachable, using a locally synced credential.
- Branch-issued JWTs validate on the LAN without a database read on every request.
- The token issuer and signing key identify one `BranchInstanceId`.
- POS actions include a user identity, not only a device identity.
- Cloud user revocation reaches Branch through downstream sync and blocks future local sessions.

It is failing if:

- POS login requires Cloud connectivity.
- Device pairing grants permission to sell without user login.
- One shared password identifies all cashiers.
- A signing key from one branch works for another branch.

## More information

- Related ADRs: [ADR-0006](0006-authentication-jwt-argon2-rbac.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0024](0024-local-http-api.md), [ADR-0027](0027-synchronization-architecture.md)
