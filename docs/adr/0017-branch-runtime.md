# ADR-0017: Branch runtime responsibilities

| Field    | Value                                         |
| -------- | --------------------------------------------- |
| Status   | Proposed                                      |
| Date     | 2026-07-12                                    |
| Deciders | Kevin Esquivel                                |
| Tags     | architecture, branch, cloud, workers, offline |

## Context and problem statement

ADR-0016 defines Cloud and Branch as runtime modes of the same .NET backend. That split needs a clear ownership map before implementation starts. Branch must handle the local operational path for a sucursal, including in-person sale APIs, local workers, and later sync. Cloud must handle tenant-wide administration, subscriptions, publishing, and the central sync endpoint. Shared domain modules must keep business behavior consistent across both modes.

**Question:** which backend capabilities run only in Branch, which run only in Cloud, and which remain shared?

## Decision drivers

- In-person operations must continue when Cloud is unavailable.
- Cloud must not sit on the critical path for a local sale.
- Branch must own local operational workers for its local PostgreSQL database.
- Cloud must own multi-tenant SaaS concerns that do not belong on a branch server.
- Domain modules, EF mappings, and command handlers must remain shared unless a runtime boundary proves otherwise.
- Future sync must have a place to run without changing the runtime model.

## Considered options

1. **Explicit Branch, Cloud, and Shared responsibility map** - runtime composition roots register only the services assigned to their mode.
2. **Cloud-first services with Branch overrides** - Cloud registers most services and Branch disables or replaces selected services.
3. **All services in both modes** - every host registers every API and worker, with policy deciding what can be used.
4. **Branch-specific domain modules** - Branch duplicates or forks modules that need local behavior.

## Decision outcome

**Chosen option:** _Explicit Branch, Cloud, and Shared responsibility map_, because it gives each runtime a narrow operational purpose while preserving shared business logic.

Branch runtime owns:

- Branch API for local terminals and local administration.
- Branch workers for local outbox, inbox, scheduler, health checks, and background maintenance.
- Future sync worker that pushes upstream and pulls approved downstream data.
- Local authentication needed to keep a branch operating during Cloud outages.
- Discovery endpoint for Tauri terminals that already know how to reach the Branch server.

Cloud runtime owns:

- Multi-tenant hosting for web administration.
- Billing and subscription workflows.
- Cloud administration APIs.
- Central catalog publishing.
- Sync hub endpoints that receive upstream changes from branches and publish downstream data.

Shared backend code owns:

- Domain modules.
- EF mappings.
- Command handlers.
- Integration event contracts.
- Cross-cutting module rules that apply to both runtimes.

### Positive consequences

- Teams can review a new API or worker against a concrete runtime owner.
- Branch servers avoid Cloud-only SaaS concerns.
- Cloud hosts avoid local discovery and local terminal services.
- Sync has an explicit home on both sides: Branch sync worker and Cloud sync hub.

### Negative consequences

- Some infrastructure must split into shared abstractions plus runtime implementations.
- Test fixtures must cover both registration graphs.
- Documentation must stay current when a worker moves from shared to runtime-specific ownership.

### Trade-offs accepted

- Binexus accepts more explicit service registration to avoid accidental runtime coupling.
- Branch local auth can differ operationally from Cloud auth, but both must produce identities the shared handlers understand.
- Future sync work may refine this map, but it must not route in-person sale traffic through Cloud.

## Pros and cons of the options

### Option 1 - Explicit Branch, Cloud, and Shared responsibility map

- **Good:** Makes runtime ownership reviewable before code exists.
- **Good:** Keeps Branch lean enough for physical or VM deployment inside a sucursal.
- **Good:** Supports ADR-0003's local hub plus cloud sync model.
- **Bad:** Requires discipline when adding new background services.
- **Bad:** Runtime ownership errors show up at startup or integration test time, not at compile time by default.

### Option 2 - Cloud-first services with Branch overrides

- **Good:** Shortens the first implementation if Cloud already has most services.
- **Good:** Reduces initial registration code.
- **Bad:** Branch becomes an exception path.
- **Bad:** Cloud assumptions leak into local operations.
- **Bad:** Disabled services are harder to audit than absent services.

### Option 3 - All services in both modes

- **Good:** Simplest registration model.
- **Good:** Fewer differences between hosts during development.
- **Bad:** Branch may expose billing, subscription, or cloud admin surfaces it should not host.
- **Bad:** Cloud may run local workers that make no sense without a branch PostgreSQL instance.
- **Bad:** Operators cannot reason about the blast radius of a deployment.

### Option 4 - Branch-specific domain modules

- **Good:** Branch can optimize local workflows without waiting for Cloud concerns.
- **Good:** Module names can mirror local operations.
- **Bad:** Business rules drift between Cloud and Branch.
- **Bad:** Sync must reconcile two interpretations of the same aggregate.
- **Bad:** Contradicts ADR-0016's shared domain module rule.

## Validation

This decision is working if:

- `AddBranchRuntime()` registers Branch API, local workers, local auth, discovery, and future sync worker services.
- `AddCloudRuntime()` registers Cloud admin, billing, central catalog, and sync hub services.
- Shared command handlers do not depend on runtime-specific services.
- Tests can list the services in each runtime and catch forbidden registrations.
- Branch can process local terminal requests while Cloud is down.

Re-evaluate this decision if:

- Shared handlers need different business rules per runtime.
- Branch exposes Cloud-only APIs to local terminals.
- Sync needs additional runtime categories that Cloud and Branch cannot express.

## More information

- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0003](0003-offline-first-design.md), [ADR-0015](0015-nestjs-retirement-dotnet-sole-backend.md), [ADR-0016](0016-runtime-modes-cloud-vs-branch.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
