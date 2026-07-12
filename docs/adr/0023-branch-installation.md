# ADR-0023: Branch installation topology

| Field    | Value                                          |
| -------- | ---------------------------------------------- |
| Status   | Proposed                                       |
| Date     | 2026-07-12                                     |
| Deciders | Kevin Esquivel                                 |
| Tags     | branch, installation, postgres, tauri, windows |

## Context and problem statement

Binexus branch runtime needs two installation shapes. One machine in the sucursal acts as the Principal Server and owns local authority. Secondary cashier machines run only the POS client and connect to the Principal over LAN.

The branch backend is always the operational authority for in-person work. Terminals never write PostgreSQL directly. Cloud does not participate in an in-person sale when a Branch runtime is present.

**Question:** what installs on the principal machine, what installs on secondary cashier machines, and how do updates, rollback, backup, and recovery work at architecture level?

## Decision drivers

- **Single local authority** - One PostgreSQL database per sucursal keeps stock, cash, and sales decisions branch-scoped.
- **Simple cashier setup** - Secondary cashiers should not install or operate PostgreSQL.
- **Operational continuity** - Branch sales, warehouse, and logistics continue without Cloud connectivity.
- **Recoverability** - Operators need a backup and restore path for the branch database and runtime secrets.
- **Small ops surface** - Fewer background services per branch reduce support burden.
- **Shared domain modules** - Cloud and Branch use the same modules, with runtime behavior selected by composition roots.

## Considered options

1. **Principal Server plus Secondary Cashier installs** - One branch server owns Postgres and the Windows Service; secondary cashiers install Tauri only.
2. **Every cashier installs PostgreSQL** - Each terminal carries its own local database and syncs with peers or Cloud.
3. **Cloud-only POS terminals** - Cashiers use Cloud APIs for every sale.
4. **Managed appliance only** - Binexus ships a locked hardware device as the only supported branch runtime.

## Decision outcome

**Chosen option:** _Principal Server plus Secondary Cashier installs_, because it gives each sucursal one operational authority while keeping cashier terminals lightweight.

### Installation roles

| Role                     | Installed components                                                                  | Responsibilities                                                                                                |
| ------------------------ | ------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Principal Server         | Branch Server, PostgreSQL, Windows Service, optional Tauri client on the same machine | Owns branch API, local database, background jobs, sync worker, backups, local certs, and branch runtime secrets |
| Secondary Cashier (Caja) | Tauri client only                                                                     | Discovers the Principal over LAN, completes device pairing, authenticates users, and calls the local HTTP API   |

The Principal Server runs the .NET modular monolith in Branch runtime mode. The same domain modules serve Cloud and Branch; composition roots select runtime services through `AddCloudRuntime` and `AddBranchRuntime`.

One PostgreSQL instance exists per sucursal. Secondary cashiers never install PostgreSQL and never receive database credentials.

### Update, rollback, backup, and recovery

Updates target the Principal Server and Tauri clients separately:

- Principal Server updates replace the Windows Service and Branch runtime package, then run compatible database migrations.
- Secondary Cashier updates replace the Tauri app and keep device credentials.
- Rollback returns the runtime package to the previous version when migrations remain backward compatible. Irreversible migrations require an explicit backup checkpoint before upgrade.
- Backups capture the branch PostgreSQL database and branch secret material needed to restore `BranchInstanceId` identity.
- Recovery restores the latest valid branch backup on the same or replacement Principal, then re-establishes sync with Cloud by idempotent checkpoints.

These points define architecture responsibilities. They do not prescribe installer UX, backup schedule, storage location, or operator runbook steps yet.

### Positive consequences

- Each sucursal has one local source of truth for stock, sales, sessions, and cash.
- Secondary cashier installation stays small and supportable.
- Branch API remains the only path from terminal to data.
- A Principal can keep operating during Cloud outage.
- The installation model matches the pairing and local API decisions.

### Negative consequences

- Principal Server failure affects the whole sucursal until recovery.
- Branch backup discipline matters because Postgres lives locally.
- LAN discovery and firewall setup become part of branch installation.
- Multi-cashier branches depend on one local machine and network segment.

### Trade-offs accepted

- Binexus accepts one stronger local machine per sucursal instead of spreading authority across cashiers.
- Binexus accepts a branch-level failure domain because it avoids peer-to-peer data conflict for stock and money.
- Binexus defers managed appliance support until customer demand justifies hardware operations.

## Pros and cons of the options

### Option 1 - Principal Server plus Secondary Cashier installs

- **Good:** One PostgreSQL per sucursal.
- **Good:** Secondary cashiers remain client-only.
- **Good:** Supports offline sales without Cloud in the request path.
- **Good:** Keeps shared modules and runtime composition aligned with ADR-0002 and ADR-0015.
- **Bad:** Principal failure stops local terminals until recovery.
- **Bad:** Requires backup and Windows Service management.

### Option 2 - Every cashier installs PostgreSQL

- **Good:** Each cashier can continue if another terminal fails.
- **Bad:** Creates multiple operational authorities inside one branch.
- **Bad:** Forces conflict resolution for stock, cash sessions, and ticket numbers.
- **Bad:** Increases installer, backup, and support work on every cashier machine.

### Option 3 - Cloud-only POS terminals

- **Good:** No local server to install.
- **Bad:** Cloud outage stops in-person sales.
- **Bad:** Violates the branch authority rule when Branch runtime is present.
- **Bad:** Pushes printer, drawer, scanner, and LAN device concerns into a cloud path.

### Option 4 - Managed appliance only

- **Good:** Binexus controls hardware and OS shape.
- **Bad:** Slows adoption for small branches that already have Windows PCs.
- **Bad:** Adds inventory, shipping, warranty, and replacement operations.

## Validation

This decision is working if:

- A branch can install one Principal Server and multiple Secondary Cashiers.
- Only the Principal has PostgreSQL and database credentials.
- Secondary cashiers complete pairing and then call the Principal Branch API over LAN.
- A Cloud outage does not block in-person sales on an installed Principal.
- A branch backup can restore local identity and data on a replacement Principal.

It is failing if:

- Every cashier needs PostgreSQL to sell.
- A terminal writes to PostgreSQL directly.
- Cloud availability becomes a requirement for `CreateSale` when Branch is present.
- Rollback requires manual database surgery for routine updates.

## More information

- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0003](0003-offline-first-design.md), [ADR-0015](0015-nestjs-retirement-dotnet-sole-backend.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0024](0024-local-http-api.md), [ADR-0027](0027-synchronization-architecture.md)
