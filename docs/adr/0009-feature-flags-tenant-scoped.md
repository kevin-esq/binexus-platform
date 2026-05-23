# ADR-0009: Feature flags — tenant-scoped, DB-backed

| Field    | Value                                              |
| -------- | -------------------------------------------------- |
| Status   | Accepted                                           |
| Date     | 2026-05-23                                         |
| Deciders | Kevin Esquivel                                     |
| Tags     | architecture, feature-flags, multi-tenant, rollout |

## Context and problem statement

Binexus is **multi-industry** by design — the same platform will eventually serve corner stores, restaurants, distributors, etc. Each industry needs a different subset of modules and behaviors. We also need to roll out new modules **per-tenant** so we can pilot a feature without exposing it to everyone.

**Question:** how do we gate functionality per tenant, without sprinkling `if (tenant.industry === "retail") { ... }` across the codebase?

## Decision drivers

- **Per-tenant on/off** — not just global. The whole point.
- **Cheap to evaluate** — guards run on every request; cannot hit the DB each time.
- **Cache-coherent** — flipping a flag for a tenant must propagate within seconds.
- **Auditable** — we should know who enabled/disabled what.
- **Replaceable** — we should be able to swap to LaunchDarkly/Unleash later without changing call sites.
- **Phase 0 simple** — no external SaaS dependency yet.

## Considered options

1. **Environment variables** — flags via `process.env.FEATURE_X`.
2. **Hardcoded in code** — `const FEATURE_X = true;`.
3. **Database-backed `TenantFeature` table + in-memory cache + invalidation hook** _(chosen)_.
4. **Third-party SaaS** — LaunchDarkly, Unleash, Flagsmith.
5. **Redis-backed flag store.**

## Decision outcome

**Chosen option:** _Database-backed `TenantFeature` table + `FeatureFlagsService` with a process-local cache and explicit invalidation hooks_.

Concretely:

- `TenantFeature` rows: `(tenantId, featureKey, enabled, config jsonb, updatedAt)`.
- `FeatureFlagsService.isEnabled(tenantId, key)` reads from a `Map<tenantId, Map<key, value>>` cache; misses load from DB.
- `FeatureFlagsService.invalidate(tenantId)` (called by admin endpoints that flip flags) clears that tenant's cache slice.
- A `FeatureFlagGuard` paired with the `@RequireFeature(FeatureKey.X)` decorator gates controllers.
- `FeatureKey` is a TypeScript `enum` in `@binexus/types` — flags are not free-form strings.

### Positive consequences

- **Per-tenant** is the default unit, not an afterthought.
- **Microsecond evaluation** in the hot path (in-process map lookup).
- **Auditable** — `TenantFeature.updatedAt` and (future) audit log give us a history.
- **Typed** — `FeatureKey` enum means a misspelled flag is a compile error, not a silent `false`.
- **Replaceable** — `FeatureFlagsService` is a single class; we can swap its internals for an Unleash client later without touching guards or controllers.

### Negative consequences

- **In-process cache** means N-node deployments need a pub/sub channel for cross-node invalidation (deferred to Phase 1+ — single-node today).
- **No percentage rollouts** out of the box (only on/off per tenant). Add a `config` blob if/when a rollout requires it.
- **No targeting rules** (only by `tenantId`). Sufficient for Phase 0.

### Trade-offs accepted

- We pay the cost of running our own flag service for the gain of zero external dependencies and microsecond hot-path cost. We accept that we will replace the internals (not the interface) once we need rich targeting.
- Cache coherence across nodes is a known gap for the day we deploy multi-node. Sketched mitigation: a Redis pub/sub channel on `feature-flags:invalidate:{tenantId}`.

## Pros and cons of the options

### Option 1 — Environment variables

- **Good:** Zero infra.
- **Bad:** Not per-tenant.
- **Bad:** Requires a redeploy to flip.

### Option 2 — Hardcoded

- **Good:** Free.
- **Bad:** Requires a redeploy to flip.
- **Bad:** Not per-tenant.
- **Bad:** Code rot — flags forgotten in `if` branches forever.

### Option 3 — `TenantFeature` table + cache _(chosen)_

- **Good:** Per-tenant, typed, cheap to evaluate.
- **Good:** No external dependency.
- **Good:** Easy to seed via Prisma seed script.
- **Bad:** We own the operational surface.
- **Bad:** Multi-node cache invalidation is on us.

### Option 4 — LaunchDarkly / Unleash / Flagsmith

- **Good:** Targeting, rollouts, audit out of the box.
- **Good:** Polished admin UI.
- **Bad:** External dependency + cost.
- **Bad:** SDK adds startup latency for flag bootstrap.
- **Bad:** Network failure mode — we cache, but we still depend on it.
- **Bad:** Overkill for Phase 0.

### Option 5 — Redis-backed

- **Good:** Multi-node coherence built-in.
- **Bad:** Adds Redis as a hard dep (we have it in Compose, but not as a hard runtime dep for the backend yet).
- **Bad:** Reinvents what (4) provides, with worse UX.

## Validation

This decision is working if:

- Flipping a flag for tenant T affects all of T's subsequent requests within ~1 second (with cache invalidation).
- A misspelled `FeatureKey` is caught at compile time.
- New industries can be onboarded with a `TenantFeature` rowset, not code changes.
- No business logic reads `process.env.FEATURE_*` directly.

It is failing if:

- Multi-node deploys lose cache coherence and we patch with hacky TTLs instead of solving invalidation properly.
- Flags accumulate forever (we need a "flag retirement" policy — TODO when we have > 20 flags).
- Targeting requirements grow (per-user, per-branch, percentage). At that point, this ADR is superseded by an integration ADR.

## More information

- [Martin Fowler — Feature toggles](https://martinfowler.com/articles/feature-toggles.html)
- [Unleash docs](https://docs.getunleash.io/) — the most likely future replacement of the internals.
- Related: ADR-0005 (multi-tenant — flags are scoped by `tenantId`), ADR-0007 (command bus — guards run before handlers).
- Related docs: [`docs/architecture/feature-flags.md`](../architecture/feature-flags.md)
