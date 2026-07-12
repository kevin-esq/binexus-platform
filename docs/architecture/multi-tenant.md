# Multi-tenant strategy

## Decision

**Shared database + `tenantId` column + request-scoped tenant context + EF filters / explicit scoping.**

We deliberately rejected "DB per tenant" because:

- DDL changes would require N migrations.
- Backups and observability multiply.
- It would block low-cost onboarding of small tenants.

We will revisit if a single tenant ever needs hard isolation guarantees (e.g. regulated industries).

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL. Nest Prisma `forTenant()` + ALS is historical ([ADR-0005](../adr/0005-multi-tenant-shared-database.md) amended by [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)). Sync reservation + JWT tenant middleware: [ADR-0014](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md).

## Mechanics (.NET)

1. **`AuthenticatedTenantMiddleware`** ([source](../../apps/backend/src/Binexus.Platform/Tenancy/AuthenticatedTenantMiddleware.cs)) binds `{ tenantId, userId, role, branchId, … }` onto `ICurrentTenant` from validated JWT claims after authentication.
2. **`DevelopmentTenantOverrideMiddleware`** is Development/Testing only and never supersedes JWT context ([ADR-0014](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md)).
3. Handlers read `ICurrentTenant.Current` and scope EF queries by `TenantId`. Cross-module inventory writes stage on the caller's `BinexusDbContext` in one transaction.

## Rules

- Business code must not query tenant-owned rows without a tenant context (except deliberate identity login / seed / migrate paths).
- Super-admin or system workers that operate across tenants set context explicitly (outbox processor sets tenant from the event envelope).
- Adding a new tenant-owned table requires an EF configuration that includes `TenantId` and the module's query discipline.
