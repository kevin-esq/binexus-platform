# ADR-0016: Runtime modes for Cloud and Branch

| Field    | Value                                      |
| -------- | ------------------------------------------ |
| Status   | Proposed                                   |
| Date     | 2026-07-12                                 |
| Deciders | Kevin Esquivel                             |
| Tags     | architecture, runtime, branch, cloud, sync |

## Context and problem statement

ADR-0003 chose local hub plus cloud sync as the offline-first direction. ADR-0002 kept Binexus as a modular monolith, and ADR-0015 retired NestJS so .NET is the only backend runtime. The next architecture phase needs one backend codebase that can run as the Cloud backend or as a Branch backend without forking the domain model. Cloud serves web administration and central operations. Branch serves in-person operations through local Tauri terminals and syncs with Cloud when the sync layer exists.

**Question:** how should the .NET backend select Cloud behavior versus Branch behavior without spreading runtime checks across modules?

## Decision drivers

- Cloud and Branch must share the same domain modules, EF mappings, command handlers, and contracts.
- Tauri terminals must call the Branch backend over HTTP and must never write PostgreSQL directly.
- Cloud must not participate in an in-person sale path.
- Runtime-specific infrastructure must live in explicit composition roots, not scattered `if (mode)` checks.
- A small team must deploy, debug, and test both modes from one codebase.
- The model must deepen ADR-0003 without changing its offline-first direction.

## Considered options

1. **Single backend artifact with explicit runtime composition roots** - one .NET backend boots Cloud or Branch services through `AddCloudRuntime()` or `AddBranchRuntime()`.
2. **Scattered runtime checks inside modules** - shared code checks `BINEXUS_RUNTIME_MODE` wherever behavior differs.
3. **Separate Cloud and Branch codebases** - Cloud and Branch live in different repositories or solution trees.
4. **Dual binaries with duplicated domain code** - Cloud and Branch compile separate binaries that copy or fork domain modules.

## Decision outcome

**Chosen option:** _Single backend artifact with explicit runtime composition roots_, because it keeps the domain model shared while isolating runtime-specific hosting, workers, and integration services.

The backend reads `BINEXUS_RUNTIME_MODE=Cloud|Branch` during startup. Startup code maps that value to one composition root:

```csharp
services.AddCloudRuntime();
services.AddBranchRuntime();
```

Each composition root registers the services for that runtime. Domain modules remain shared and runtime-neutral. Runtime-specific infrastructure belongs near the composition root or in runtime-specific platform packages, not inside command handlers or aggregates.

### Positive consequences

- Cloud and Branch stay one product, one domain language, and one testable backend codebase.
- Branch behavior can exclude billing, tenant-wide admin, and cloud sync hub services at startup.
- Cloud behavior can exclude local discovery, local auth bootstrap, and Branch-only workers at startup.
- Architecture tests can assert that modules do not branch on `BINEXUS_RUNTIME_MODE`.

### Negative consequences

- Startup becomes a real architectural boundary and needs tests.
- Engineers must decide whether new infrastructure belongs to Cloud, Branch, or shared module registration.
- A misregistered service can make a runtime appear healthy while missing a worker or exposing an API it should not expose.

### Trade-offs accepted

- Binexus accepts a slightly richer startup model to keep domain modules clean.
- Binexus treats runtime selection as deployment configuration, not a domain concept.
- The first Branch runtime may share more plumbing than it needs, then narrow registrations as Branch-only operations mature.

## Pros and cons of the options

### Option 1 - Single backend artifact with explicit runtime composition roots

- **Good:** Preserves ADR-0002's modular monolith and ADR-0015's single .NET backend.
- **Good:** Keeps runtime-specific registrations visible and reviewable.
- **Good:** Lets tests start Cloud and Branch hosts from the same solution.
- **Bad:** Startup code carries more responsibility than a flat service-registration file.
- **Bad:** Local development must make the selected runtime obvious.

### Option 2 - Scattered runtime checks inside modules

- **Good:** Fast to add one-off behavior.
- **Good:** Avoids an initial composition-root design pass.
- **Bad:** Runtime policy leaks into command handlers and domain services.
- **Bad:** Branch-only behavior becomes hard to audit.
- **Bad:** A future sync worker would inherit hidden Cloud assumptions.

### Option 3 - Separate Cloud and Branch codebases

- **Good:** Each deployment can optimize independently.
- **Good:** Runtime ownership looks simple at repository level.
- **Bad:** Duplicates domain rules and EF mappings.
- **Bad:** Increases migration cost for every aggregate and command.
- **Bad:** Undermines the single-backend decision in ADR-0015.

### Option 4 - Dual binaries with duplicated domain code

- **Good:** Packaging can differ by target environment.
- **Good:** Each binary can expose a narrow surface.
- **Bad:** Domain duplication creates drift in sales, inventory, and sync invariants.
- **Bad:** Bug fixes must land twice.
- **Bad:** Offline-first contracts stop being a shared guarantee.

## Validation

This decision is working if:

- Cloud startup registers only Cloud runtime services through `AddCloudRuntime()`.
- Branch startup registers only Branch runtime services through `AddBranchRuntime()`.
- Domain modules compile and test without reading `BINEXUS_RUNTIME_MODE`.
- Integration tests can boot both runtime modes from the same backend solution.
- A Tauri terminal can complete a local HTTP sale flow without Cloud availability once Branch sales exists.

Re-evaluate this decision if:

- Runtime checks appear inside aggregates, command handlers, or EF mappings.
- Branch needs a separate domain model to preserve correctness.
- Cloud and Branch release cadence diverges enough that one artifact blocks operations.

## More information

- Related ADRs: [ADR-0002](0002-modular-monolith-architecture.md), [ADR-0003](0003-offline-first-design.md), [ADR-0015](0015-nestjs-retirement-dotnet-sole-backend.md)
- Related docs: [`docs/architecture/dotnet-backend.md`](../architecture/dotnet-backend.md)
