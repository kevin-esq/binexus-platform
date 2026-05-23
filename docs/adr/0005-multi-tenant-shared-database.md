# ADR-0005: Multi-tenant — shared database + `tenantId` + `AsyncLocalStorage`

| Field    | Value                                             |
| -------- | ------------------------------------------------- |
| Status   | Accepted                                          |
| Date     | 2026-05-23                                        |
| Deciders | Kevin Esquivel                                    |
| Tags     | architecture, multi-tenant, security, persistence |

## Context and problem statement

Binexus is multi-tenant by design: every customer (a "tenant" — a business, with potentially many branches) shares one running platform. Every row of business data must be unambiguously owned by exactly one tenant. Cross-tenant data leaks would be catastrophic, both legally and reputationally.

**Question:** how do we model tenancy in the database, in code, and in the request lifecycle — such that a cross-tenant leak is hard to write by accident?

## Decision drivers

- **Cheap onboarding** of small tenants — no per-tenant infra spin-up.
- **Single set of migrations** — one DDL change, applied once.
- **Hard to leak across tenants by accident** — the default behavior of every query must be tenant-scoped.
- **Future escape hatch** — a regulated or huge tenant must be migratable to its own DB without rewriting business code.
- **Visible at code review time** — tenant scoping must be obvious in the diff.

## Considered options

1. **Database per tenant.**
2. **Schema per tenant** (shared DB, separate Postgres schemas).
3. **Shared database + `tenantId` column on every row + RLS** (Postgres Row-Level Security).
4. **Shared database + `tenantId` column + Prisma extension that auto-injects the filter from `AsyncLocalStorage`.**

## Decision outcome

**Chosen option:** _Shared DB + `tenantId` column + Prisma extension + `AsyncLocalStorage`-bound request context_. Concretely:

- A `TenantContextMiddleware` decodes the JWT and binds `{ tenantId, userId, role, branchId, requestId }` into an `AsyncLocalStorage` for the request's lifetime.
- `PrismaService.forTenant()` returns a `$extends`-wrapped client that **auto-injects** `where: { tenantId }` on reads/writes and `data: { tenantId }` on creates for every model listed in the explicit `TENANT_SCOPED_MODELS` allow-list.
- Cross-tenant code paths (super-admin, login flow, outbox dispatcher) must opt out explicitly with `@SkipTenant()` — and are responsible for their own filters.

### Positive consequences

- The default in business code is **always** tenant-scoped — forgetting `tenantId` produces no rows, not a leak.
- A single migration path; one DB to back up.
- Adding a new tenant is an `INSERT INTO tenants ...`, not a DevOps task.
- The `TENANT_SCOPED_MODELS` allow-list is **intentional friction** — adding a tenant-owned model requires a deliberate edit.

### Negative consequences

- Noisy-neighbor risk: one tenant's workload affects others (mitigated by indexes, query plans, and eventual connection pool partitioning).
- A single corrupted row or migration affects every tenant.
- We don't get Postgres RLS as a second line of defense (see "considered options").

### Trade-offs accepted

- We rely on application-level scoping. We mitigate this with: (a) the `TENANT_SCOPED_MODELS` allow-list, (b) `@SkipTenant()` being grep-able in code review, (c) integration tests that assert cross-tenant queries return empty.
- We accept that hard isolation for a specific tenant is a _future migration_, not a Phase 0 capability.

## Pros and cons of the options

### Option 1 — Database per tenant

- **Good:** Hardest possible isolation.
- **Good:** Per-tenant backups, scaling, migrations are trivial.
- **Bad:** N copies of every DDL — onboarding a new tenant requires running every migration.
- **Bad:** Observability multiplies — N pg_stat views, N pools.
- **Bad:** Disqualifies a low-touch SMB onboarding flow.

### Option 2 — Schema per tenant

- **Good:** Logical isolation without N databases.
- **Bad:** Prisma's tooling around multi-schema is not first-class.
- **Bad:** Migrations still N-way.
- **Bad:** Cross-tenant analytics (e.g. internal dashboards) become awkward.

### Option 3 — Shared DB + `tenantId` + RLS

- **Good:** Application bugs are caught by the DB itself.
- **Good:** Second line of defense.
- **Bad:** Prisma + RLS support is workable but not idiomatic; requires `SET LOCAL` per transaction.
- **Bad:** Performance impact of RLS policies on hot tables can be measurable.
- **Bad:** Connection pool semantics (`SET LOCAL`) require care with pgbouncer.

### Option 4 — Shared DB + `tenantId` + Prisma extension _(chosen)_

- **Good:** Default behavior is tenant-scoped; safe to write.
- **Good:** Excellent Prisma DX.
- **Good:** Explicit `TENANT_SCOPED_MODELS` allow-list keeps the surface visible.
- **Good:** `@SkipTenant()` is grep-able and PR-reviewable.
- **Bad:** No DB-level safety net (no RLS). Bugs in the extension or accidental raw queries can leak.
- **Bad:** Requires team discipline (never call `this.<model>` directly).

## Validation

This decision is working if:

- A new bounded context's handlers never need to reference `tenantId` explicitly — they call `prisma.forTenant().<model>` and it just works.
- `@SkipTenant()` shows up in code review and triggers a discussion every time.
- Integration tests prove that a query made under `tenantContext.run({ tenantId: A }, ...)` cannot see data created under tenant B.
- `TENANT_SCOPED_MODELS` is updated whenever we add a tenant-owned model.

It is failing if:

- We start seeing `where: { tenantId: ... }` manually written in business code.
- A bug ships that returns another tenant's data — at which point we add Postgres RLS as a second line of defense and supersede this ADR.
- Raw SQL queries in business code bypass the extension.

## More information

- [Multi-tenant data architecture (Azure / Microsoft)](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/considerations/data-architecture)
- [Prisma client extensions](https://www.prisma.io/docs/orm/prisma-client/client-extensions)
- [Postgres RLS](https://www.postgresql.org/docs/current/ddl-rowsecurity.html) — kept on the table as a future safety net.
- Related: ADR-0006 (auth — provides the JWT that carries `tenantId`), ADR-0007 (command bus — handlers rely on the context).
- Related docs: [`docs/architecture/multi-tenant.md`](../architecture/multi-tenant.md)
