# ADR-0019: Device identity for Branch and Tauri hosts

| Field    | Value                                   |
| -------- | --------------------------------------- |
| Status   | Proposed                                |
| Date     | 2026-07-12                              |
| Deciders | Kevin Esquivel                          |
| Tags     | architecture, identity, devices, branch |

## Context and problem statement

The Branch architecture introduces physical and logical machines that need durable identity. A Tauri terminal, an office workstation, and the Branch Server are devices from an operational security perspective. Users, terminals, branches, and branch instances already describe other concepts, so overloading any of them for machine identity would confuse auditing, pairing, and revocation.

**Question:** how should Binexus identify and revoke machines that participate in Branch operations?

## Decision drivers

- Device identity must survive application restarts and normal software updates.
- A user account must not double as a machine credential.
- A terminal label such as Caja 1 must not double as a machine credential.
- The Branch Server itself needs a device identity separate from the branch it serves.
- Operators need to revoke a lost, replaced, or compromised machine without deleting users or terminal roles.
- Identifiers must fit the platform direction of UUIDv7 for persistent IDs.

## Considered options

1. **Dedicated Device identity with stable UUIDv7 DeviceId** - each paired machine receives a device record and credential.
2. **Use User identity for machines** - each terminal signs in as a special user.
3. **Use Terminal identity for machines** - the terminal label or `TerminalId` identifies the physical device.
4. **Use Branch or BranchInstance identity for machines** - every machine in a branch shares branch-level identity.

## Decision outcome

**Chosen option:** _Dedicated Device identity with stable UUIDv7 DeviceId_, because machines need their own lifecycle, pairing state, credentials, and revocation.

A Device is a physical or logical machine running either a Tauri app or the Branch Server. Binexus assigns each Device a `DeviceId` using UUIDv7 during pairing or server provisioning. The `DeviceId` remains stable after pairing and survives restarts. Device is distinct from:

- User, which identifies a person or service actor with permissions.
- Terminal, which identifies a logical POS or branch role such as Caja 1.
- Branch, which identifies the sucursal.
- BranchInstance, which identifies a deployed branch runtime instance when that concept becomes necessary for sync and operations.

Revocation happens at the device level. Revoking a device blocks that machine's credentials without deleting the user, terminal, branch, or branch instance.

### Positive consequences

- Audit logs can record both the user and the device involved in an operation.
- A stolen laptop or replaced POS machine can be revoked directly.
- Terminal labels can change without breaking device credentials.
- The Branch Server can authenticate as a machine without pretending to be a user.

### Negative consequences

- Pairing, credential storage, and revocation need dedicated UX and backend state.
- Support staff must understand the difference between revoking a user and revoking a device.
- Device records can become stale unless branch operations include decommissioning steps.

### Trade-offs accepted

- Binexus accepts another identity concept because device lifecycle differs from user, terminal, and branch lifecycle.
- Device identity does not decide which cashier is acting. User identity still owns that responsibility.
- Device identity does not decide the POS role. Terminal identity still owns that responsibility.

## Pros and cons of the options

### Option 1 - Dedicated Device identity with stable UUIDv7 DeviceId

- **Good:** Separates machine lifecycle from person, terminal, and branch lifecycle.
- **Good:** Supports device-level revocation.
- **Good:** Lets audit records include both `UserId` and `DeviceId`.
- **Bad:** Adds pairing and device-management work.
- **Bad:** Requires secure local credential storage in Tauri and on the Branch Server.

### Option 2 - Use User identity for machines

- **Good:** Reuses existing authentication concepts.
- **Good:** May reduce initial schema and UI work.
- **Bad:** Blurs people and machines in audit trails.
- **Bad:** Revoking a device can accidentally revoke a user or require fake users.
- **Bad:** Weakens least-privilege modeling.

### Option 3 - Use Terminal identity for machines

- **Good:** Matches how operators talk about POS stations.
- **Good:** Avoids a separate device registry at first.
- **Bad:** A terminal is a role, not a machine.
- **Bad:** Device replacement or shift handoff breaks identity history.
- **Bad:** One physical machine can serve different terminal roles over time.

### Option 4 - Use Branch or BranchInstance identity for machines

- **Good:** Simple for branch-level sync authentication.
- **Good:** Avoids per-device enrollment.
- **Bad:** Compromise of one terminal affects every machine in the branch.
- **Bad:** Revocation cannot target one laptop or one server.
- **Bad:** Audit records lose the machine that performed the action.

## Validation

This decision is working if:

- Every Tauri installation and Branch Server has a stable UUIDv7 `DeviceId` after pairing or provisioning.
- Auth and audit records can include `DeviceId` separately from `UserId`, `TerminalId`, and `BranchId`.
- Revoking a device blocks only that machine's credentials.
- Replacing a machine does not require changing the terminal identity or branch identity.
- The system can list active, revoked, and retired devices per branch.

Re-evaluate this decision if:

- Device identity starts carrying user permissions or terminal role semantics.
- Operators cannot manage devices without high support burden.
- Sync authentication requires a separate machine identity model that Device cannot cover.

## More information

- Related ADRs: [ADR-0003](0003-offline-first-design.md), [ADR-0016](0016-runtime-modes-cloud-vs-branch.md), [ADR-0018](0018-branch-server.md), [ADR-0020](0020-terminal-identity.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
