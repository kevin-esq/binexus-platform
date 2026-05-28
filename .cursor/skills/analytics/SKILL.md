---
name: analytics
description: Event tracking, dashboards, and product analytics for Binexus across the panel, landing, and (future) driver app. Use when wiring tracking on a new flow (signup, onboarding wizard, first dispatch), when designing F8 Reporting dashboards, when computing tenant-level KPIs (DAU, activation rate, time-to-first-dispatch, on-time delivery %), when adding cohort analysis, or when reviewing whether a feature is actually used. Pairs with `documents` (XLSX exports), `growth-seo` (organic), `cro` (CRO experiments).
---

# analytics (Binexus)

Event tracking + dashboards for Binexus. Two distinct surfaces:

1. **Internal product analytics** (what Binexus uses to operate) — feeds [`F8 Reporting`](../../../docs/domains/reporting.md) and the founder dashboard.
2. **Tenant-facing analytics** (what each tenant sees on their `/reporting` page) — multi-tenant scoped, exposed via SDK.

Adapted from [`skills/marketingskills-main/skills/analytics/SKILL.md`](../../../skills/marketingskills-main/skills/analytics/SKILL.md).

## When to invoke

- Wiring tracking on a new feature (signup, onboarding step, first dispatch).
- Designing a tenant dashboard for F8.
- Building cohort or funnel analysis for go-to-market decisions.
- Picking the analytics stack (PostHog vs Mixpanel vs Snowplow + Metabase).
- Defining the "activated" event (the single event that says a tenant is sticking).
- Reviewing whether a feature is actually used before iterating on it.

## Stack recommendation (revisit at F8 kickoff)

| Need                           | Recommendation     | Why                                                                                                 |
| ------------------------------ | ------------------ | --------------------------------------------------------------------------------------------------- |
| Event tracking (web + backend) | PostHog            | Self-host on existing infra; multi-tenant via project per tenant or property-level; LATAM-friendly. |
| Tenant-facing dashboards       | Metabase           | Read replica → Metabase → embedded iframe in tenant `/reporting`. Permissioned per tenant.          |
| Funnel / cohort                | PostHog (built-in) | Saves a separate query layer.                                                                       |
| Driver app                     | PostHog mobile SDK | Same backend; respects `expo-secure-store` for the device ID.                                       |

Open question for F8 kickoff: self-host PostHog vs PostHog Cloud. Default: self-host on the existing infrastructure once we have one ops engineer.

## Event taxonomy

Names follow the established Binexus convention from [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md): `<noun>_<past-tense-verb>`. Mirror the event-bus event names where possible — same vocabulary across the system.

### Backend-emitted (mirror the event bus)

When the outbox dispatcher writes a domain event, also track an analytics event. Use the same name. Decouple the dispatcher from the analytics client — wrap in a separate listener so analytics failures never block the bus.

```
order_created
order_approved
inventory_reserved
picking_completed
order_ready_for_delivery_route
delivery_route_dispatched
delivery_confirmed
order_delivered
delivery_failed       (F4 future)
invoice_issued        (F7 future)
tenant_created
tenant_upgraded       (F7 future)
tenant_churned        (F7 future)
```

### Frontend-emitted (panel)

UI-specific events, NOT domain events. Use them for activation / engagement analysis.

```
page_viewed                  { route, tenantId, userRole }
panel_search                  { query, contextSlug }
onboarding_step_completed     { step, totalSteps }
first_action_completed        { action: 'create_order' | 'create_branch' | ... }
upgrade_clicked               { tier, source }
```

### Landing / public

Identify only after a known event (signup), not on every page view. Until then, treat as anonymous.

```
landing_page_viewed           { path, referrer, utm_*, device }
cta_clicked                   { ctaSlug, location }
signup_started                { plan }
signup_completed              { tenantId, plan }
```

### Driver app

```
route_received                { routeId, stopCount }
stop_confirmed                { stopId, online: boolean }
photo_captured                { stopId, sizeKb }
sync_queue_flushed            { itemsFlushed, durationMs }
```

## Property discipline

Every event carries (at minimum):

- `tenantId` — the multi-tenant key. Filter by this everywhere. Tracking that does not have it is a bug.
- `userId` — the human acting. Anonymous on landing until signup.
- `sessionId` — UUID per browser/device session.
- `appVersion` — git SHA in `apps/web`, semver in `apps/mobile`.
- `clientTimestamp` — ISO 8601 with timezone.

Avoid:

- High-cardinality properties (`requestId`, `orderId` directly). Use them as event ids, not properties. Otherwise the analytics warehouse blows up.
- PII (email, name, address). PostHog has identification flows; don't shovel raw PII into properties.

## The activation event

For Binexus: **first delivery confirmed**. A tenant that confirms one delivery is far more likely to retain than one that only creates an order. Mark the event:

```ts
posthog.capture('tenant_activated', { tenantId, daysSinceSignup });
```

Track Time-To-Activation (TTA). North-star metric for the first 6 months.

## Top KPIs (per Binexus phase)

| Phase        | KPI                                              | Where                                                             |
| ------------ | ------------------------------------------------ | ----------------------------------------------------------------- |
| Pre-launch   | Landing → Signup conversion %                    | PostHog funnel                                                    |
| Trial        | Time-to-Activation                               | PostHog cohort, days from `signup_completed` → `tenant_activated` |
| Trial → Paid | Trial → Paid conversion % per cohort             | PostHog cohort                                                    |
| Live tenants | Weekly active operator users                     | PostHog DAU                                                       |
| F4           | On-time delivery % per tenant                    | Metabase from Postgres                                            |
| F4           | Avg stops per route                              | Metabase                                                          |
| F7           | MRR, churn, expansion                            | Stripe + PostHog                                                  |
| F8           | Tenant-facing dashboard SLO (queries return <2s) | DataDog / OpenTelemetry                                           |

## Tenant-facing analytics (F8)

When F8 ships, each tenant gets a `/reporting` page. Rules:

- Read from a read-replica, never from the primary.
- Every query is `WHERE tenantId = $1` — non-negotiable. Use Prisma's `forTenant()` even in the analytics layer.
- Dashboards live in Metabase (or PostHog dashboards); embed via per-tenant signed iframe.
- Tenant cannot see other tenants' aggregate (no benchmarking visible). If benchmarking ships later, it's anonymized at the row level.

## Privacy

- Cookie banner on the landing — track only after consent (LATAM is GDPR-adjacent + local laws; consent is a legal requirement).
- `Do Not Track` browser header respected.
- PII never in event properties — only in the auth user record.
- Retention: 18 months default. Configurable per tenant on Scale tier.

## Anti-patterns

- Calling `posthog.capture` directly from a domain handler. Wrap in an event listener; never couple the domain to the analytics client.
- Emitting `*_clicked` for every button. Track only the ones tied to a KPI.
- Custom-naming events ("user-logged-in-with-google-on-friday"). Stick to the canonical names.
- Sharing the analytics secret in `.env.local`. Use the per-environment secret manager.
- Dashboards with no owner. Every dashboard has a named owner who reviews monthly. Orphan dashboards rot.

## Pre-PR checklist

- [ ] New events declared in `packages/analytics/event-catalog.md` (create when this skill first ships).
- [ ] `tenantId` + `userId` + `sessionId` on every event.
- [ ] No PII in properties.
- [ ] Wrapped via a listener — domain code doesn't import the analytics client.
- [ ] Naming follows `<noun>_<past-tense-verb>`.
- [ ] If the event is critical for revenue (activation, signup, invoice), there's a backend mirror as well, not only frontend.

## Reference

- [`skills/marketingskills-main/skills/analytics/SKILL.md`](../../../skills/marketingskills-main/skills/analytics/SKILL.md)
- [`.cursor/skills/documents/SKILL.md`](../documents/SKILL.md) — XLSX exports of analytics data
- [`.cursor/skills/cro/SKILL.md`](../cro/SKILL.md) — funnel + AB test definitions
- [`.cursor/skills/growth-seo/SKILL.md`](../growth-seo/SKILL.md) — landing-side tracking
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md) — backend event catalog
- [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md)
