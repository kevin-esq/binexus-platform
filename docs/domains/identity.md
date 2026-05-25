# Identity domain

Status: **active** (Phase 0). Bounded context: `identity`.

Identity owns who can access the platform and under which tenant/branch context. It is intentionally small: authentication, tenant membership, branch membership, and role metadata.

## Owns

- `Tenant` - business account and top-level isolation boundary.
- `Branch` - physical operating location inside a tenant.
- `User` - human account scoped to a tenant and optionally a branch.
- `RefreshToken` - hashed, revocable, single-use refresh token.
- `Role` - RBAC enum used by guards.

## Does not own

- Customer records. Those belong to [`customers`](customers.md).
- Employees/payroll. Future HR domain, not Phase 1.
- Feature entitlements. Those live in tenant-scoped feature flags.

## Commands

Implemented / foundation:

- `LoginCommand` (currently service-level, can be formalized later).
- `RefreshSessionCommand`.
- `LogoutCommand`.

Future:

- `RegisterUserCommand`.
- `DisableUserCommand`.
- `AssignUserToBranchCommand`.

## Events emitted

- `USER_REGISTERED`.

## Events consumed

None in Phase 0. Identity should avoid consuming operational events unless the event changes access or account state.

## Allowed dependencies

- May be read by authorization infrastructure.
- Other contexts may use authenticated request claims (`tenantId`, `userId`, `role`, `branchId`) through `TenantContextService`.
- Other contexts may not mutate identity tables.

## Boundary rules

1. Login is one of the few valid `@SkipTenant()` flows because the tenant is resolved during login.
2. Every authenticated request must bind tenant/user/role/branch to the tenant context before hitting business code.
3. A context can reference `userId` or `branchId`, but it must not join directly to `User` or `Branch` for domain logic.
4. Historical records store IDs and snapshots where needed; changing a user name does not rewrite old order approvals.

## System users (automation actor)

Each tenant should have exactly one `User` with `isSystem: true` (email `system@<tenantSlug>.system`). Created by [`apps/backend/prisma/seed.ts`](../../apps/backend/prisma/seed.ts) today. Event-driven handlers in other contexts (e.g. Orders auto-cancel on `INVENTORY_RESERVATION_FAILED`) resolve this user via `SystemUserService`.

**TODO:** When `RegisterTenantCommand` ships, provision the system user in the same transaction as tenant creation (not only via seed).

## Open questions

- Do we need invitation-based onboarding before self-registration?
- Should `SUPER_ADMIN` live outside tenants entirely, or as a platform tenant?
