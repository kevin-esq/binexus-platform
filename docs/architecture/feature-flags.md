# Feature flags

## Decision

Feature flags are **tenant-scoped**, persisted in Identity's `tenant_features` table, cached in-process (TTL 30s), and read through `ITenantFeatureService` from **`Binexus.Platform.Features.Contracts`**. Identity implements the port; it does not own the commercial API.

## Why

- Binexus is multi-industry. Not every tenant uses POS, Routes, Warehouse, or Billing.
- Activating a feature is a business event, not a deployment. Sales/onboarding needs to flip flags without touching code.
- Per-tenant rollout lets us pilot new modules on selected tenants.

## Pieces (.NET)

| Piece                                                       | Where                                                                                                                  |
| ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `FeatureKey` / `FeatureKeyValues` / `ITenantFeatureService` | [`apps/backend/src/Binexus.Platform.Features.Contracts/`](../../apps/backend/src/Binexus.Platform.Features.Contracts/) |
| `TenantFeature` entity + EF config                          | Identity Domain / Infrastructure                                                                                       |
| `TenantFeatureService` (cache + DB)                         | Identity Infrastructure                                                                                                |
| Wire keys (TS)                                              | [`packages/types/src/features.ts`](../../packages/types/src/features.ts)                                               |

## Legacy

Nest `FeatureFlagsService` / `@RequireFeature()` lived under `apps/backend` — removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md). Wire keys in `@binexus/types` remain.

## Lifecycle

1. **Define**: add the key to `FeatureKey` in `packages/types/src/features.ts` and `FeatureKey` / `FeatureKeyValues` in Platform.Features.Contracts.
2. **Seed**: Development seeder upserts a `TenantFeature` row with `enabled: false` for every key.
3. **Enable**: call `ITenantFeatureService.SetEnabledAsync(tenantId, FeatureKey.X, true)` (tests/seed/admin).
4. **Gate**: handlers call `IsEnabledAsync` with the JWT tenant (e.g. Sales `POS_RETAIL`, Logistics `LIQUIDATION`).

## Cache

- TTL: 30 seconds per `(tenantId, key)`.
- `TenantFeatureService.Invalidate(tenantId, key?)` clears entries — call after toggling.
- The cache is in-process and per-instance. Multi-node needs a pub/sub invalidation channel later.

## Rules

- **No commercial entitlements gated by env vars / appsettings.** Use `TenantFeature` (ops kill switches are separate — e.g. Logistics liquidation kill switch).
- **Flags ARE NOT permissions.** Permissions come from `Role`. A feature flag says "the tenant bought this module"; a role says "the user is allowed to use this verb".
- **Default-off.** New features ship disabled, opt in per tenant.
- **ADR:** [`docs/adr/0009-feature-flags-tenant-scoped.md`](../adr/0009-feature-flags-tenant-scoped.md)
