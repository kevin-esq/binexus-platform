# Feature flags

## Decision

Feature flags are **tenant-scoped**, persisted in `TenantFeature`, cached in-process (TTL 30s), and read through `FeatureFlagsService`. Endpoints are gated with `@RequireFeature('KEY')`.

## Why

- Binexus is multi-industry. Not every tenant uses POS, Routes, Warehouse, or Billing.
- Activating a feature is a business event, not a deployment. Sales/onboarding needs to flip flags without touching code.
- Per-tenant rollout lets us pilot new modules on selected tenants.

## Pieces

| Piece                 | Where                                                                                                                                      |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `TenantFeature` model | [`apps/backend/prisma/schema.prisma`](../../apps/backend/prisma/schema.prisma)                                                             |
| `FeatureKey` enum     | [`packages/types/src/features.ts`](../../packages/types/src/features.ts)                                                                   |
| `FeatureFlagsService` | [`apps/backend/src/common/feature-flags/feature-flags.service.ts`](../../apps/backend/src/common/feature-flags/feature-flags.service.ts)   |
| `@RequireFeature()`   | [`apps/backend/src/common/decorators/require-feature.decorator.ts`](../../apps/backend/src/common/decorators/require-feature.decorator.ts) |
| `FeatureFlagGuard`    | [`apps/backend/src/common/feature-flags/feature-flag.guard.ts`](../../apps/backend/src/common/feature-flags/feature-flag.guard.ts)         |

## Lifecycle

1. **Define**: add the key to `FeatureKey` in `packages/types/src/features.ts`.
2. **Seed**: the seed script creates a `TenantFeature` row with `enabled: false` for every tenant. Adding a new key requires running the seed (or a small migration).
3. **Enable**: call `featureFlagsService.setEnabled(tenantId, key, true)` from a super-admin endpoint or admin UI.
4. **Gate**: decorate the relevant controller / handler with `@RequireFeature('KEY')` and apply `FeatureFlagGuard`.

## Cache

- TTL: 30 seconds per `(tenantId, key)`.
- `featureFlagsService.invalidate(tenantId, key?)` clears entries — call after toggling.
- The cache is in-process and per-instance. When we run multiple backend instances we'll add a Redis pub/sub invalidation channel.

## Usage example (Phase 1+)

```ts
@Controller('orders')
@UseGuards(FeatureFlagGuard)
@RequireFeature(FeatureKey.ORDERS)
export class OrdersController {
  // every endpoint here requires the tenant to have ORDERS enabled
}
```

## Rules

- **No code paths gated by env vars per feature.** Use `TenantFeature`.
- **Flags ARE NOT permissions.** Permissions come from `Role`. A feature flag says "the tenant bought this module"; a role says "the user is allowed to use this verb".
- **Default-off.** New features ship disabled, opt in per tenant. Easy to roll out, easy to roll back.
