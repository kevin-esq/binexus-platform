# ADR-0020: Terminal identity as a logical POS role

| Field    | Value                                   |
| -------- | --------------------------------------- |
| Status   | Proposed                                |
| Date     | 2026-07-12                              |
| Deciders | Kevin Esquivel                          |
| Tags     | architecture, identity, terminal, sales |

## Context and problem statement

The Sales module already uses `TerminalId` as a free-string value object on `SalesSession` per ADR-0013 and the .NET backend architecture notes. The Branch architecture adds device identity, Tauri terminals, and one Branch Server per sucursal. Binexus needs to clarify whether a terminal is a physical device or a logical POS role such as Caja 1. That decision affects sales session invariants, audit history, device replacement, and shift handoff.

**Question:** should Terminal identify a machine, a user-facing POS role, or something else?

## Decision drivers

- Existing SalesSession behavior treats `TerminalId` as part of the branch cash-session invariant.
- Device identity now covers the physical or logical machine running Tauri or Branch Server.
- Operators think in terminal roles such as Caja 1, Caja 2, Oficina, or Mostrador.
- A branch may replace hardware without changing cash-session history.
- A branch may hand off a terminal role between devices over time.
- The current free-string value object must evolve carefully to avoid breaking existing sales behavior.

## Considered options

1. **Terminal as logical POS role, distinct from Device** - terminal identifies Caja 1 or similar branch role, and devices can serve that role over time.
2. **Terminal as physical device** - terminal identity and device identity are the same concept.
3. **Terminal as user session** - terminal identity exists only while a cashier is signed in.
4. **No Terminal identity beyond labels** - sales stores a display string with no managed terminal concept.

## Decision outcome

**Chosen option:** _Terminal as logical POS role, distinct from Device_, because terminal names describe branch operations while device identity describes machines.

A Terminal is a logical POS or branch workstation role within one branch. Labels such as Caja 1, Caja 2, Oficina, or Mostrador are operator-facing names for that role. `TerminalId` today remains the free-string value object on `SalesSession`; Binexus will evolve it carefully from that shape when Terminal becomes a managed entity or reference data concept.

Multiple devices may serve the same Terminal over time. This supports hardware replacement, shift handoff, and temporary use from an office workstation. At any point, Branch policy may restrict active terminal assignment or active SalesSession behavior, but Terminal identity itself does not equal Device identity. The existing invariant remains centered on one open SalesSession per `(TenantId, BranchId, TerminalId)`.

### Positive consequences

- Sales history remains tied to the business role operators recognize.
- Device replacement does not rewrite cash-session history.
- Audit logs can show `DeviceId`, `TerminalId`, and `UserId` separately.
- Branches can model real operations where a role moves between machines over time.

### Negative consequences

- Pairing and assignment UX must explain the difference between device and terminal.
- Runtime policy must prevent confusing concurrent use of the same terminal role.
- The current free-string `TerminalId` needs a migration path when Terminal becomes managed data.

### Trade-offs accepted

- Binexus accepts that Terminal has business meaning even before it becomes a first-class aggregate or catalog record.
- Binexus permits multiple devices to serve the same terminal over time, but local policy can restrict simultaneous active use.
- The free-string value object remains valid until a later ADR or migration defines the managed Terminal model.

## Pros and cons of the options

### Option 1 - Terminal as logical POS role, distinct from Device

- **Good:** Matches operator language and SalesSession invariants.
- **Good:** Keeps machine revocation in Device identity.
- **Good:** Supports handoff and hardware replacement.
- **Bad:** Requires assignment rules between device and terminal.
- **Bad:** Needs clear UI copy to avoid support confusion.

### Option 2 - Terminal as physical device

- **Good:** Simple one-to-one model for small branches.
- **Good:** Avoids an assignment layer at first.
- **Bad:** Conflicts with ADR-0019's Device concept.
- **Bad:** Hardware replacement changes terminal identity.
- **Bad:** Shift handoff between devices becomes a data-model problem.

### Option 3 - Terminal as user session

- **Good:** Captures who is operating at a moment in time.
- **Good:** Could fit simple single-cashier deployments.
- **Bad:** A terminal role exists before and after one user session.
- **Bad:** SalesSession already owns cash-session lifecycle separately from user login.
- **Bad:** Audit loses the branch workstation role.

### Option 4 - No Terminal identity beyond labels

- **Good:** Lowest initial modeling cost.
- **Good:** Keeps current string storage with no migration.
- **Bad:** Cannot manage allowed terminals per branch.
- **Bad:** Makes validation and reporting dependent on label spelling.
- **Bad:** Makes future device-terminal assignment harder.

## Validation

This decision is working if:

- SalesSession continues to use `TerminalId` as the branch cash-session role.
- Device management can revoke a machine without deleting or renaming a terminal.
- Audit records can include separate `UserId`, `DeviceId`, `TerminalId`, and `BranchId` values.
- Branch UI can show and manage terminal labels without exposing machine credentials.
- Tests preserve one open SalesSession per `(TenantId, BranchId, TerminalId)`.

Re-evaluate this decision if:

- Branch operators consistently treat each terminal role as permanently tied to one machine.
- Simultaneous use of one terminal role from multiple devices creates unacceptable operational risk that assignment rules cannot prevent.
- The free-string `TerminalId` blocks reporting, authorization, or sync correctness.

## More information

- Related ADRs: [ADR-0003](0003-offline-first-design.md), [ADR-0013](0013-sales-pos-sub-slices-and-session-model.md), [ADR-0016](0016-runtime-modes-cloud-vs-branch.md), [ADR-0019](0019-device-identity.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
