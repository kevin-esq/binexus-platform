# Identity domain

Status: **active** (Phase 0). Bounded context: `identity`.

Identity owns who can access the platform and under which tenant/branch context. The active .NET module lives at `apps/backend/src/Modules/Binexus.Modules.Identity` and owns authentication, tenant membership, branch membership, and role metadata.

## Owns

- `Tenant` - business account and top-level isolation boundary.
- `Branch` - physical operating location inside a tenant.
- `User` - human account scoped to a tenant and optionally a branch.
- `RefreshToken` - hashed, revocable, single-use refresh token.
- `Role` - one of `SUPER_ADMIN`, `ADMIN`, `CASHIER`, `WAREHOUSE`, `DRIVER`.

## Does not own

- Customer records. Those belong to [`customers`](customers.md).
- Employees/payroll. Future HR domain, not Phase 1.
- Feature entitlements. Those live in tenant-scoped feature flags.

## HTTP surface

- `POST /auth/login` — resolves the tenant by slug and returns access + refresh tokens.
- `POST /auth/refresh` — rotates a single-use opaque refresh token.
- `POST /auth/logout` — revokes the presented refresh token; access tokens are not blacklisted.
- `GET /auth/me` — returns the current user, tenant, and optional branch.

Login and refresh use the `auth` rate-limit policy. Logout and me require a valid HS256 access token.

## Authentication invariants

- Passwords use Argon2id PHC (`v=19`, `m=65536`, `t=3`, `p=4`).
- Refresh tokens are random 256-bit Base64Url values; only their SHA-256 hashes are stored.
- Refresh rotation is atomic. Reuse of a used or revoked token revokes the entire family.
- Access claims remain `sub`, `tenantId`, `role`, and `branchId`, plus standard issuer, audience, issued-at, expiry, and token ID claims.
- JWT signing keys come from `Jwt__SigningKey` or user-secrets. The committed settings contain no key.
- Email lookup uses trimmed, Unicode-normalized, invariant uppercase `NormalizedEmail`, unique per tenant.

## Commands

Implemented through `IAuthService`:

- Login.
- Refresh session.
- Logout.

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

1. Login and refresh run before a tenant is known; login resolves the tenant by slug and refresh resolves it from the stored token.
2. Every authenticated request must bind tenant/user/role/branch to the tenant context before hitting business code.
3. A context can reference `userId` or `branchId`, but it must not join directly to `User` or `Branch` for domain logic.
4. Historical records store IDs and snapshots where needed; changing a user name does not rewrite old order approvals.

## System users (automation actor)

The Identity migration preserves `User.IsSystem`. The idempotent development seed creates the authorized `acme` tenant, `Main` branch, and `admin@acme.test` super-admin; provisioning a dedicated automation actor remains future work.

**TODO:** When `RegisterTenantCommand` ships, provision the system user in the same transaction as tenant creation (not only via seed).

## Open questions

- Do we need invitation-based onboarding before self-registration?
- Should `SUPER_ADMIN` live outside tenants entirely, or as a platform tenant?
