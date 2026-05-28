---
name: cro
description: Conversion rate optimization for the Binexus public funnel (landing → signup → trial → activated tenant). Use when designing A/B tests, when optimizing CTAs / popups / paywalls, when reducing signup friction, when adding lead magnets or free tools, when reviewing why a page does not convert, or when prioritizing growth experiments. Pairs with `marketing-copy` (variants), `analytics` (event tracking), `ui-ux-pro` (UX patterns).
---

# cro (Binexus)

Conversion-rate optimization for Binexus's public funnel. Adapted from the merged upstream skills `cro`, `ab-testing`, `popups`, `paywalls`, `free-tools`, `lead-magnets`, `signup` (see [`skills/marketingskills-main/skills/cro/SKILL.md`](../../../skills/marketingskills-main/skills/cro/SKILL.md), [`ab-testing`](../../../skills/marketingskills-main/skills/ab-testing/SKILL.md), [`popups`](../../../skills/marketingskills-main/skills/popups/SKILL.md), [`paywalls`](../../../skills/marketingskills-main/skills/paywalls/SKILL.md), [`free-tools`](../../../skills/marketingskills-main/skills/free-tools/SKILL.md), [`lead-magnets`](../../../skills/marketingskills-main/skills/lead-magnets/SKILL.md), [`signup`](../../../skills/marketingskills-main/skills/signup/SKILL.md)).

## When to invoke

- Designing an A/B test on the landing, signup, or pricing.
- Optimizing the trial signup flow (form fields, default plan, social auth).
- Considering a popup / exit-intent modal / paywall.
- Building a lead magnet (e.g. "Calculadora de rutas") or free tool.
- Diagnosing why a page does not convert.
- Prioritizing the growth backlog.

## The funnel (Binexus baseline)

| Stage                             | Conversion target (Y1) | Current source of truth                                             |
| --------------------------------- | ---------------------- | ------------------------------------------------------------------- |
| Landing → Signup started          | 4-8 %                  | PostHog funnel (when [`analytics`](../analytics/SKILL.md) wires it) |
| Signup started → Signup completed | 50-70 %                | PostHog funnel                                                      |
| Signup completed → Activated      | 25-40 %                | PostHog funnel                                                      |
| Activated → Paid                  | 15-25 %                | Stripe + PostHog                                                    |

These are SaaS-industry medians for B2B 50-500 ARR / month. Adjust after the first 200 trials.

## Experiment discipline

### Hypothesis template

Every experiment starts with a written hypothesis:

```
Because [insight from analytics or research],
if we change [variable],
then [metric] will [direction] by [magnitude],
because [mechanism].
```

Example:

> Because 62% of signups drop at the "company name" field (PostHog funnel),
> if we move company name to the wizard's first step instead of the signup form,
> then signup completion will increase from 54% to >65%,
> because the form will feel like a 2-field signup (email + password).

### Power + duration

- Minimum sample: 1,000 visitors per variant for landing tests, 200 signups per variant for trial-flow tests.
- Minimum duration: 7 days (captures weekly cycle).
- Maximum duration: 28 days (otherwise external factors swamp the signal).
- Stop early ONLY if the variant trends >95% worse than control on the primary metric AND day ≥ 3.

### Tracking

Each experiment uses PostHog's experiment feature (or an in-house feature flag wrapping the same event vocabulary). Required events from [`analytics`](../analytics/SKILL.md):

- `experiment_assigned` (variant, experimentId)
- the primary metric event (e.g. `signup_completed`)
- the guardrail metric event (e.g. `tenant_activated` — to ensure the variant doesn't win signup but lose activation)

## What to optimize, in order

The order matters. Bottom-of-funnel wins compound faster than top-of-funnel.

1. **Signup form friction**. Each field has a cost. Cut to the bone.
2. **Time to first action** in the onboarding wizard.
3. **Pricing page layout** — tier-feature parity, CTA placement, currency selector.
4. **Hero CTA** — wording, color, placement, single vs multiple.
5. **Trust elements** — testimonials, logos, security badges.
6. **Page speed** — every 100ms of LCP costs ~1% of conversion.
7. **Lead magnets / free tools** — for net-new traffic, not optimization of existing.

## Signup form rules

| Decision                        | Default                                                                |
| ------------------------------- | ---------------------------------------------------------------------- |
| Number of fields                | 3 (email, password, tenant name). Anything else lives in the wizard.   |
| Social auth                     | Google + Microsoft. Apple optional (driver app justifies it later).    |
| Email verification              | Optional — block dispatching until verified, allow read-only browsing. |
| Plan selection                  | Default to trial. Tier choice in the wizard or after trial.            |
| Captcha                         | hCaptcha invisible; show full only on suspected abuse.                 |
| Password rules                  | 12+ chars. Allow paste. No "must include uppercase" theatrics.         |
| Terms / privacy                 | Single checkbox, both links. NEVER pre-checked.                        |
| "Already have an account?" link | Visible, not buried.                                                   |

## Popups

Default: **no popups**. The landing is read by operators who hate them.

Exceptions:

- **Exit-intent on the pricing page** — one variant, single-line value prop + CTA. After 1 dismissal, suppress for 30 days for that visitor.
- **Cookie banner** — legally required in LATAM. Use a small, dismissible banner. Never a modal.

Forbidden:

- Newsletter signup popups on the landing.
- "Stay updated!" modals over the hero.
- Sticky bars at the top of the panel.
- "Spin to win" / lottery patterns.

## Paywalls (F7 era)

When F7 ships and tiers go live, the "paywall" surface is the **upgrade modal** inside the panel.

- Triggered when the tenant hits a soft cap (80 %) or hard cap (110 %).
- Tells the tenant exactly what's blocked and why ("Has alcanzado 110 % de tu cuota de órdenes en Starter. Suben tus pedidos arriba de 1,000/mes — pasa a Growth para seguir.")
- Two CTAs: upgrade + "send invoice for overage" (Scale tier behavior).
- Never a hard wall mid-action. The current action completes; the next action shows the paywall.

## Lead magnets

When organic traffic motion starts (post F6), add 1-2 lead magnets per persona. Examples that fit Binexus:

- "Calculadora de costo por entrega" — interactive widget that returns a number.
- "Plantilla Excel: hoja de ruta diaria para flotas pequeñas" — that's the pain we replace, ironically.
- "Guía: Cómo cambiar de Tookan a Binexus en 24 horas" — comparison content.

A lead magnet that doesn't ask for email is just a free tool. That's also fine — see below.

## Free tools

Free tools are a high-leverage SEO bet — they earn links and capture top-of-funnel intent without a gated form. Examples:

- A public "ETA estimator" that consumes a route as input (no auth) and returns ETAs.
- A "load planner" free version (without DB persistence).

Free tools live on `apps/web/src/app/(public)/tools/<slug>/`. They are first-class routes with full SEO treatment.

## A/B test catalog (start here)

| Test                                                         | Phase     | Likely variant winner                     |
| ------------------------------------------------------------ | --------- | ----------------------------------------- |
| Hero CTA: "Empieza prueba gratis" vs "Ver demo en 2 min"     | Landing   | Trial CTA — stronger primary              |
| Pricing page: anchored MXN/USD vs USD-only                   | Pricing   | Anchored MXN/USD (LATAM)                  |
| Signup: 3 fields vs 5 fields                                 | Signup    | 3 fields                                  |
| Onboarding: "create branch first" vs "use sample data first" | Wizard    | Use sample data first (faster activation) |
| Trial expiry email: T-7 vs T-3 vs T-1 vs all three           | Lifecycle | All three (compounding)                   |

## Anti-patterns

- Running tests with no hypothesis ("just see what happens").
- Running tests with no guardrail metric — signups go up but activation collapses.
- Stopping a test on day 2 because "it looks like the variant is winning".
- Running 5 tests on the same page at once. Interaction effects swamp results.
- Optimizing for clicks instead of activated tenants.
- A/B testing the pricing while sales is also discounting — adds noise.
- Popups that block the page during initial load.

## Pre-PR checklist

- [ ] Hypothesis written, with insight + variable + metric + magnitude + mechanism.
- [ ] Sample + duration plan documented.
- [ ] Primary + guardrail metrics wired in [`analytics`](../analytics/SKILL.md).
- [ ] Variant doesn't break the SEO of the page (`canonical` unchanged, structure unchanged).
- [ ] Cleanup task scheduled: how/when the losing variant is removed.

## Reference

- [`skills/marketingskills-main/skills/cro/SKILL.md`](../../../skills/marketingskills-main/skills/cro/SKILL.md)
- [`skills/marketingskills-main/skills/ab-testing/SKILL.md`](../../../skills/marketingskills-main/skills/ab-testing/SKILL.md)
- [`skills/marketingskills-main/skills/popups/SKILL.md`](../../../skills/marketingskills-main/skills/popups/SKILL.md)
- [`skills/marketingskills-main/skills/paywalls/SKILL.md`](../../../skills/marketingskills-main/skills/paywalls/SKILL.md)
- [`skills/marketingskills-main/skills/free-tools/SKILL.md`](../../../skills/marketingskills-main/skills/free-tools/SKILL.md)
- [`skills/marketingskills-main/skills/lead-magnets/SKILL.md`](../../../skills/marketingskills-main/skills/lead-magnets/SKILL.md)
- [`skills/marketingskills-main/skills/signup/SKILL.md`](../../../skills/marketingskills-main/skills/signup/SKILL.md)
- [`.cursor/skills/analytics/SKILL.md`](../analytics/SKILL.md) — event tracking + funnel
- [`.cursor/skills/marketing-copy/SKILL.md`](../marketing-copy/SKILL.md) — variants
- [`.cursor/skills/ui-ux-pro/SKILL.md`](../ui-ux-pro/SKILL.md) — signup + wizard structure
- [`.cursor/skills/pricing/SKILL.md`](../pricing/SKILL.md) — pricing-page experiments
