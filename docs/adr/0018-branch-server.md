# ADR-0018: Branch Server per sucursal

| Field    | Value                                          |
| -------- | ---------------------------------------------- |
| Status   | Proposed                                       |
| Date     | 2026-07-12                                     |
| Deciders | Kevin Esquivel                                 |
| Tags     | architecture, branch, postgres, tauri, offline |

## Context and problem statement

ADR-0003 chose a local hub for offline-first operation. ADR-0016 and ADR-0017 define Branch as a .NET runtime that owns local operations for a sucursal. The desktop phase adds Tauri terminals such as Caja 1, Caja 2, and Oficina. Those terminals need a local HTTP endpoint, but they must not own the database or run private copies of backend logic.

**Question:** what process and database topology should a physical branch use?

## Decision drivers

- A sucursal needs one local authority for sales, cash sessions, inventory movements, and sync state.
- Tauri terminals must use HTTP only to the Branch backend.
- Tauri terminals must never write PostgreSQL directly.
- The branch must use one PostgreSQL database per sucursal, not one database per terminal.
- The runtime must run on practical branch hardware, either a physical machine or a VM.
- Operators need a service model that can start on boot and recover after restarts.

## Considered options

1. **One Branch Server per sucursal with one local PostgreSQL database** - a physical host or VM runs Branch API and workers as service processes.
2. **One server per terminal** - each Tauri terminal runs or owns its own backend server.
3. **Embedded SQLite inside each Tauri app** - each terminal stores operational data locally and syncs later.
4. **Cloud-only POS path** - Tauri terminals call Cloud for every sale and local action.

## Decision outcome

**Chosen option:** _One Branch Server per sucursal with one local PostgreSQL database_, because it gives the branch one transactional authority while keeping terminals thin and replaceable.

Each sucursal runs one Branch Server host on a physical machine or VM. That host runs the Branch API and Branch workers as Windows Service processes or an equivalent service supervisor for the target environment. The Branch Server owns the single PostgreSQL instance for that sucursal. Tauri terminals call the Branch API over HTTP for sales, cash sessions, local health, and future branch operations. No Tauri process opens a PostgreSQL connection.

### Positive consequences

- The branch has one place to enforce transactional invariants.
- Terminal replacement does not require database migration.
- Local workers can process outbox, inbox, scheduler, and future sync against one database.
- Operational support can monitor one server process and one PostgreSQL instance per branch.

### Negative consequences

- The Branch Server becomes a local dependency for all terminals in that sucursal.
- The branch needs hardware, service installation, backup, and health checks.
- A Branch Server outage stops local terminal operations until failover or repair exists.

### Trade-offs accepted

- Binexus accepts a local server dependency because per-terminal databases would make stock, cash, and sync correctness harder.
- High availability inside one branch is a later operations problem, not a reason to put databases on terminals.
- Tauri stays a client shell for UI and local device integration, not an application server.

## Pros and cons of the options

### Option 1 - One Branch Server per sucursal with one local PostgreSQL database

- **Good:** Matches ADR-0003's local hub choice.
- **Good:** Keeps relational invariants inside PostgreSQL and EF transactions.
- **Good:** Gives sync one upstream source for the branch.
- **Bad:** Requires server installation and local operations discipline.
- **Bad:** One local host can fail and affect every terminal in the branch.

### Option 2 - One server per terminal

- **Good:** Each terminal can appear self-contained.
- **Good:** A terminal can continue if another terminal fails.
- **Bad:** Creates multiple local authorities for the same branch.
- **Bad:** Forces reconciliation between Caja 1, Caja 2, and Oficina before Cloud sync even starts.
- **Bad:** Increases support cost for every additional terminal.

### Option 3 - Embedded SQLite inside each Tauri app

- **Good:** Simple local packaging for one-device demos.
- **Good:** Terminal can start without a separate server.
- **Bad:** Conflicts with the selected PostgreSQL and EF backend architecture.
- **Bad:** Turns each terminal into a sync participant with its own conflict set.
- **Bad:** Makes cash and inventory invariants harder to enforce across terminals.

### Option 4 - Cloud-only POS path

- **Good:** No local server to install.
- **Good:** Cloud observability sees every request immediately.
- **Bad:** Violates ADR-0003's offline-first goal.
- **Bad:** Cloud becomes part of in-person sale latency and availability.
- **Bad:** A connectivity outage stops sales.

## Validation

This decision is working if:

- Each branch deployment plan contains one Branch Server host and one PostgreSQL instance.
- Tauri code has no PostgreSQL credentials, drivers, or direct database access.
- Branch API health shows the local PostgreSQL, worker, and scheduler state.
- Integration tests exercise terminal HTTP calls through Branch API boundaries.
- Operational runbooks can start, stop, back up, and restore a branch database.

Re-evaluate this decision if:

- A common branch deployment cannot provide any stable host or VM.
- One Branch Server cannot support the expected number of local terminals after measured load testing.
- Regulatory or hardware constraints require a different local authority model.

## More information

- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0003](0003-offline-first-design.md), [ADR-0016](0016-runtime-modes-cloud-vs-branch.md), [ADR-0017](0017-branch-runtime.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
