# Architecture overview

## Mantra

> **Foundation wide. Execution narrow.**

The architecture is designed to expand for five years. Features ship one bounded context at a time, focused and minimal.

## Stack (current)

| Layer          | Choice                                              |
| -------------- | --------------------------------------------------- |
| Backend        | C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL  |
| Web            | Next.js (App Router) + `@binexus/sdk` → Api `:5102` |
| Workers        | `Binexus.Workers` (outbox/inbox)                    |
| Object storage | MinIO (S3-compatible)                               |
| Auth           | JWT (access + refresh) + RBAC                       |

NestJS is **not** a supported runtime. Legacy backend: NestJS, removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md) migration.

## System shape

```mermaid
flowchart LR
    subgraph clients[Clients]
        web[Web app<br/>Next.js]
        desktop[Desktop<br/>Tauri 2]
        mobile[Mobile<br/>placeholder]
    end

    subgraph backend[Backend monolith - .NET 10]
        api[HTTP API<br/>ASP.NET Core :5102]
        workers[Workers<br/>outbox/inbox :5103]
        identity[Identity]
        orders[Orders]
        catalog[Catalog<br/>planned]
        customers[Customers<br/>planned]
        sales[Sales]
        inventory[Inventory]
        warehouse[Warehouse]
        logistics[Logistics]
        billing[Billing<br/>planned]
        reporting[Reporting<br/>planned]
        outbox[Outbox<br/>Postgres]
        ef[EF Core<br/>tenant-scoped]
    end

    subgraph infra[Infrastructure - Docker Compose]
        pg[("Postgres 16")]
        minio[("MinIO<br/>object storage")]
    end

    web --> api
    desktop --> api
    mobile -. future .-> api
    api --> identity
    api --> catalog
    api --> customers
    api --> orders
    api --> sales
    api --> inventory
    api --> warehouse
    api --> logistics
    api --> billing
    api --> reporting
    identity --> outbox
    orders --> outbox
    sales --> outbox
    inventory --> outbox
    warehouse --> outbox
    logistics --> outbox
    outbox --> workers
    workers --> identity
    workers --> orders
    workers --> inventory
    workers --> warehouse
    workers --> logistics
    identity --> ef
    orders --> ef
    sales --> ef
    inventory --> ef
    warehouse --> ef
    logistics --> ef
    ef --> pg
    api --> minio
```

Detail: [`dotnet-backend.md`](./dotnet-backend.md). Bounded contexts: [`bounded-contexts.md`](./bounded-contexts.md).

## Target (Branch Runtime — Proposed)

Three installation modes: **Cloud Runtime**, **Branch Server**, **Branch Client**. See [`branch-runtime.md`](./branch-runtime.md) and [`../migration/branch-runtime-architecture-checkpoint.md`](../migration/branch-runtime-architecture-checkpoint.md).

```mermaid
flowchart LR
    subgraph cloud[Cloud Runtime]
        webAdmin[Web Admin]
        cloudApi[Cloud API / Workers]
    end

    subgraph branch[Branch Server]
        principal[Branch API + Workers + Sync]
        branchPg[(PostgreSQL local)]
        principal --> branchPg
    end

    subgraph terminals[Branch Clients]
        caja1[Tauri Caja 1]
        caja2[Tauri Caja 2]
        oficina[Tauri Oficina]
    end

    webAdmin --> cloudApi
    principal <-->|Sync journal| cloudApi
    caja1 -->|TLS + device + user| principal
    caja2 -->|TLS + device + user| principal
    oficina -->|TLS + device + user| principal
```

Cloud is off the in-person sale path. Branch Client never writes PostgreSQL directly.

## Why a modular monolith

- Single founder, evolving domain — microservices' cost dwarfs their benefit.
- Cross-context invariants stay correctable as long as we don't network-split prematurely.
- Bounded contexts inside the monolith give us a clean **extraction surface** later: lift a context into a service once it has its own operational requirements.

## Why event-driven from day one

The operational surface (offline POS, route liquidation, returns, multi-step approvals) cannot be modeled with synchronous request/response without coupling everything. Events keep contexts independent and provide a natural audit trail.

## Why offline-first

Real LATAM operations have unreliable connectivity. The architecture allows for a future **local hub → cloud sync** topology. Phase 0 doesn't ship sync code, but it bakes in the contracts (event envelopes with `correlationId`, idempotent commands, branch-scoped writes) that make it possible later without rewrites.
