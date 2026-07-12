# ADR-0016: Three installation modes - Cloud Runtime, Branch Server, Branch Client

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Binexus needs cloud SaaS control and local branch operations. Operators and docs previously said "modo local" for both the authoritative branch host and a secondary cashier. That conflation breaks installer design, offline semantics, and security.

## Decision

Binexus uses three installation modes:

```text
Cloud Runtime
Branch Server
Branch Client
```

### Cloud Runtime

```text
.NET API Cloud
.NET Workers Cloud
PostgreSQL Cloud
```

Owns SaaS administration, activations, entitlements, consolidation, sync ingest and publish, Web Admin, and future public surfaces.

### Branch Server

```text
.NET Branch API
.NET Branch Workers
.NET Sync Worker
PostgreSQL local
```

Owns authoritative in-person operations for one sucursal. Tauri may run on the same machine. Branch Server does not depend on Tauri being open.

### Branch Client

```text
Tauri Desktop
```

Connects to Branch Server over LAN HTTP(S). Does not run PostgreSQL and does not hold an authoritative domain copy.

Do not use "modo local" for Branch Server and Branch Client interchangeably.

## Composition roots

```csharp
services.AddBinexusCore();

switch (runtimeMode)
{
    case RuntimeMode.Cloud:
        services.AddCloudRuntime();
        break;

    case RuntimeMode.Branch:
        services.AddBranchRuntime();
        break;
}
```

`AddBinexusCore()` registers shared domain modules, handlers, validation, and EF mappings that do not read `RuntimeMode`.

Shared modules must not contain:

```csharp
if (runtimeMode == RuntimeMode.Branch) { ... }
```

inside handlers or aggregates. Runtime-specific ports bind only inside `AddCloudRuntime()` / `AddBranchRuntime()`.

Branch Client is not a .NET host mode. It is a Tauri installation that talks to Branch Server.

## Capability matrix

| Capability                                                           | Cloud | Branch Server | Shared |
| -------------------------------------------------------------------- | :---: | :-----------: | :----: |
| Multi-tenant SaaS admin                                              |   x   |               |        |
| Activations / entitlements                                           |   x   |               |        |
| Web Admin API                                                        |   x   |               |        |
| Public / e-commerce intake                                           |   x   |               |        |
| Sync ingest + downstream publish                                     |   x   |               |        |
| Consolidation / reporting store                                      |   x   |               |        |
| In-person sale commit                                                |       |       x       |        |
| Local PostgreSQL authority                                           |       |       x       |        |
| LAN HTTP API for clients                                             |       |       x       |        |
| Branch Sync Worker (push/pull)                                       |       |       x       |        |
| Local user auth (synced hashes)                                      |       |       x       |        |
| Device pairing issuer                                                |       |       x       |        |
| Domain modules / handlers / EF maps                                  |       |               |   x    |
| Outbox for in-process events                                         |       |               |   x    |
| OpenAPI command shapes (subset)                                      |       |               |   x    |
| Identity / Orders / Sales / Inventory / Warehouse / Logistics models |       |               |   x    |

### Endpoints

| Class        | Examples                                                       | Host                            |
| ------------ | -------------------------------------------------------------- | ------------------------------- |
| Shared shape | CreateSale, stock adjustments (same contracts where both run)  | Cloud and/or Branch composition |
| Cloud-only   | tenant billing, activation codes, catalog publish, sync ingest | Cloud                           |
| Branch-only  | LAN device pairing, local discovery metadata, branch health    | Branch Server                   |

### Workers

| Class          | Examples                                           | Host                       |
| -------------- | -------------------------------------------------- | -------------------------- |
| Shared pattern | outbox dispatcher                                  | both, different transports |
| Cloud-only     | SaaS jobs, consolidation, sync hub                 | Cloud                      |
| Branch-only    | Sync Worker upstream/downstream, local maintenance | Branch Server              |

## Architecture tests (future)

- Modules under `src/Modules` do not reference `RuntimeMode` or `BINEXUS_RUNTIME_MODE`.
- Cloud DI does not register Branch-only LAN/discovery services.
- Branch DI does not register Cloud-only billing/public ingress services.

## Consequences

### Positive

- Clear installer and UX language.
- Composition roots keep domain code free of deployment conditionals.

### Negative / Trade-offs

- Two .NET composition paths to maintain.
- Branch Client behavior lives in Tauri docs, not `RuntimeMode`.

## Alternatives considered

1. **Single "Local" mode for server and client** - Rejected: hides authority and offline boundaries.
2. **Separate .NET products with forked modules** - Rejected: duplicates domain.
3. **`if (runtimeMode)` inside handlers** - Rejected: scatters deployment policy into domain.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
