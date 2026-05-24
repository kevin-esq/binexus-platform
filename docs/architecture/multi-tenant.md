# Multi-tenant strategy

## Decision

**Shared database + `tenantId` column + `AsyncLocalStorage` request context + Prisma extension.**

We deliberately rejected "DB per tenant" because:

- DDL changes would require N migrations.
- Backups and observability multiply.
- It would block low-cost onboarding of small tenants.

We will revisit if a single tenant ever needs hard isolation guarantees (e.g. regulated industries). At that point, the same code can opt that tenant into a dedicated DB via the `DATABASE_URL` resolved per-context.

## Mechanics

1. **`TenantContextMiddleware`** ([source](../../apps/backend/src/common/tenant/tenant-context.middleware.ts)) decodes the JWT and binds `{ tenantId, userId, role, branchId, requestId }` to an `AsyncLocalStorage`.
2. **`PrismaService.forTenant()`** ([source](../../apps/backend/src/common/prisma/prisma.service.ts)) returns a `$extends`-wrapped client that auto-injects `where: { tenantId }` on read/write and `data: { tenantId }` on create for every tenant-scoped model.
3. **`TENANT_SCOPED_MODELS`** is the explicit allow-list. Adding a model that holds tenant data requires adding it to that set — this is intentional friction.

## Tenant-scoped models

- `User`
- `Branch`
- `OutboxEvent`
- `TenantFeature`
- `Order`
- `OrderLine`
- `OrderTransition`
- `AuditLog`

Foundation models that are NOT tenant-scoped:

- `Tenant` itself
- `RefreshToken` (scoped via the `User` it belongs to)

## Rules

- **Never call `this.<model>` directly from business code.** Always go through `prisma.forTenant().<model>`. The non-extended client is reserved for `identity` (login flow), super-admin tooling, and the outbox dispatcher.
- **Super-admin endpoints** that must operate across tenants are marked with `@SkipTenant()`. They are responsible for explicitly providing `tenantId` filters when needed.
- **Cross-tenant reads are a code smell.** Surface them as ADRs.

## Example

```ts
// Inside a command handler:
@Injectable()
export class CreateBranchHandler {
  constructor(private readonly prisma: PrismaService) {}

  async execute(cmd: CreateBranchCommand): Promise<BranchId> {
    // tenantId is injected automatically from AsyncLocalStorage.
    const branch = await this.prisma.forTenant().branch.create({
      data: { name: cmd.name },
    });
    return branch.id as BranchId;
  }
}
```

## What to do / what NOT to do

| Do                                                           | Don't                                                            |
| ------------------------------------------------------------ | ---------------------------------------------------------------- |
| Use `prisma.forTenant()` in every command/query handler      | Read `tenantId` from a request param and pass it manually        |
| Add new tenant-scoped models to `TENANT_SCOPED_MODELS`       | Trust client-provided `tenantId`                                 |
| Use `@SkipTenant()` for super-admin endpoints                | Bypass `forTenant()` "just for this query"                       |
| Test handlers by running them inside `tenantContext.run({})` | Use `process.env.CURRENT_TENANT` or globals to thread the tenant |
