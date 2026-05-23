# Architecture overview

## Mantra

> **Foundation wide. Execution narrow.**

The architecture is designed to expand for five years. Features ship one bounded context at a time, focused and minimal.

## System shape (Phase 0)

```mermaid
flowchart LR
    subgraph clients[Clients]
        web[Web app<br/>Next.js 15]
        desktop[Desktop<br/>Tauri 2]
        mobile[Mobile<br/>placeholder]
    end

    subgraph backend[Backend monolith - NestJS 11]
        api[HTTP API<br/>Fastify]
        identity[Identity context<br/>active in F0]
        orders[Orders context<br/>F1]
        sales[Sales context<br/>F5]
        inventory[Inventory context<br/>F2]
        logistics[Logistics context<br/>F4-6]
        bus[Event bus<br/>in-process]
        outbox[Outbox<br/>Postgres table]
        prisma[PrismaService<br/>tenant-scoped]
    end

    subgraph infra[Infrastructure - Docker Compose]
        pg[("Postgres 16")]
        redis[("Redis 7.4<br/>future event transport")]
        minio[("MinIO<br/>object storage")]
    end

    web --> api
    desktop --> api
    mobile -. future .-> api
    api --> identity
    api --> orders
    api --> sales
    api --> inventory
    api --> logistics
    identity --> bus
    orders --> bus
    sales --> bus
    inventory --> bus
    logistics --> bus
    bus --> outbox
    outbox -. dispatcher F1+ .-> redis
    identity --> prisma
    orders --> prisma
    sales --> prisma
    inventory --> prisma
    logistics --> prisma
    prisma --> pg
    backend --> minio
```

## Why a modular monolith

- Single founder, evolving domain — microservices' cost dwarfs their benefit.
- Cross-context invariants stay correctable as long as we don't network-split prematurely.
- Bounded contexts inside the monolith give us a clean **extraction surface** later: lift a context into a service once it has its own operational requirements.

## Why event-driven from day one

The operational surface (offline POS, route liquidation, returns, multi-step approvals) cannot be modeled with synchronous request/response without coupling everything. Events keep contexts independent and provide a natural audit trail.

## Why offline-first

Real LATAM operations have unreliable connectivity. The architecture allows for a future **local hub → cloud sync** topology. Phase 0 doesn't ship sync code, but it bakes in the contracts (event envelopes with `correlationId`, idempotent commands, branch-scoped writes) that make it possible later without rewrites.
