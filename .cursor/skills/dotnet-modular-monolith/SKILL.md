---
name: dotnet-modular-monolith
description: Architecture and layering guardrails for the Binexus .NET modular monolith (`apps/backend/` — Api, Platform, Modules, SharedKernel, Workers). Use when writing or reviewing C# under apps/backend/, adding a module or feature slice, wiring Program.cs middleware, EF/migrations, multi-tenancy, outbox/commands, health checks, CORS, rate limiting, or NetArchTest boundaries. Pairs with `dotnet-clean-code` and `semantic-naming`.
---

# dotnet-modular-monolith (Binexus)

Rules for the .NET modular monolith. More abstractions are not the goal — preserve extractable module seams and hard-to-leak tenancy.

## Dependency graph (non-negotiable)

```text
SharedKernel     → nobody
Platform         → SharedKernel          (never Modules)
Modules.*        → SharedKernel (+ Platform only for shared contracts)
Api / Workers    → Platform + Modules
Domain           → never Infrastructure
```

**Forbidden:** Platform → Modules, SharedKernel → anything else, Domain → Infrastructure / AspNetCore / EF.

Cross-module needs = event / `*.Contracts` / SharedKernel port / dispatcher — **not** a project reference to another module’s Domain/Infrastructure. Architecture tests (NetArchTest) must fail on violations.

Import style: consumers take a ProjectReference to the Contracts assembly and write a normal `using` (`using Binexus.Modules.Orders.Contracts;`). Do **not** spam fully-qualified type names as a boundary ritual — that is style noise, not architecture. Exceptions: name collisions, one-offs, and string/meta (EF relationship names, NetArchTest namespace filters, migrations).

## Module shape

Follow Identity / Inventory:

| Layer              | Owns                                      | Must not own      |
| ------------------ | ----------------------------------------- | ----------------- |
| **Domain**         | Entities, invariants, business enums      | EF, HTTP, JWT, DI |
| **Application**    | Ports (`IAuthService`), DTOs, error codes | SQL, AspNetCore   |
| **Infrastructure** | EF configs, services, hash, JWT, seed     | HTTP endpoints    |
| **Features/**      | Minimal APIs (`MapXEndpoints`)            | Domain rules      |

`Program.cs` only composes: `AddXModule` + `MapXEndpoints`. Zero business rules there.

## Multi-tenancy (security #1)

- Tenant **only** from validated JWT → `ICurrentTenant` (`AuthenticatedTenantMiddleware`).
- **Never** trust `X-Tenant-Id` in Production (Dev/Testing override only).
- Business queries filter by context `tenantId`; body/query `tenantId` is a leak smell.
- Exceptions: login, seeder, outbox dispatcher, explicit super-admin.
- Always `Clear()` tenant at end of request (`finally`).

## Write recipe (CQRS-lite)

Every use case:

1. Validate input
2. Open transaction
3. Load aggregate / apply invariant
4. Stage outbox **in the same TX**
5. Single `SaveChanges` / commit

No “commit then publish”. Idempotency (`OperationKey` / conditional claim) on anything retriable.

Reads = simple queries. No event-sourcing / projections until pain is real.

## Persistence

- EF configurations in Infrastructure; Domain does not know `DbContext`.
- Per-module `IDbContextModelContributor` — Platform never references modules.
- UUIDv7 from app when that is the slice convention.
- Composite indexes with `tenantId` first on hot tables.
- Migrations append-only; one conscious slice per migration.

## Hosting / API pipeline

Fixed order:

`ForwardedHeaders` → Security → Routing → CORS → RateLimit → AuthN → Tenant → (Dev override) → AuthZ → endpoints

- Problem Details with stable `code`s.
- Liveness `/health` trivial; readiness `/health/ready` checks Postgres.
- Rate limit after forwarded headers; configure `TrustedProxies` / `TrustedNetworks` in prod.
- Options: `ValidateOnStart` for secrets / JWT / CORS.

## Concurrency & money/stock

- Optimistic concurrency or conditional claims (`WHERE UsedAtUtc IS NULL`).
- Integration + concurrency tests for reserve / liquidate / refresh rotation.
- Expected business failures → `Result` / domain codes → HTTP at the edge (not exceptions).

## Testing pyramid

| Kind              | Covers                                   |
| ----------------- | ---------------------------------------- |
| ArchitectureTests | Reference graph                          |
| Unit              | Domain, hasher, normalizers — no DB      |
| Integration       | HTTP + Postgres (Testcontainers)         |
| Concurrency       | Parallel TX / requests on critical paths |

Slice done = happy path + business rejection + multi-tenant case.

## Ops / observability

- Serilog: `tenantId`, `userId`, correlation / trace. Never passwords or tokens.
- Outbox workers off the request path; claim/lock with reclaim.
- Secrets via env / user-secrets only.

## Product discipline

- One bounded context = one module; vertical slices, not giant horizontal layers.
- Architecture changes → short ADR.
- Close slices with checkpoint docs (`docs/migration/*`).
- Mantra: **Foundation wide. Execution narrow.**

## Pre-PR checklist

- [ ] No forbidden project references?
- [ ] Tenant from JWT only (prod)?
- [ ] Outbox staged in same TX as write?
- [ ] Endpoints thin; invariants in Domain/Application?
- [ ] Architecture + integration (+ concurrency if critical) green?

## See also

- Coding style: [`../dotnet-clean-code/SKILL.md`](../dotnet-clean-code/SKILL.md)
- Naming: [`../semantic-naming/SKILL.md`](../semantic-naming/SKILL.md)
- ADRs: [`../../../docs/adr/0002-modular-monolith-architecture.md`](../../../docs/adr/0002-modular-monolith-architecture.md), [`../../../docs/adr/0005-multi-tenant-shared-database.md`](../../../docs/adr/0005-multi-tenant-shared-database.md), [`../../../docs/adr/0007-command-bus-cqrs-lite.md`](../../../docs/adr/0007-command-bus-cqrs-lite.md)
