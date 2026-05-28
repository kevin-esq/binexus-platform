---
name: pricing
description: Pricing strategy, packaging, and tier design for Binexus — both the public landing pricing page and the Stripe billing schema. Use when the user asks about pricing tiers, free trial limits, per-seat vs per-route vs per-tenant pricing, packaging features per tier, value-metric selection, discount/promo design, or when modifying the F7 Billing context. Pairs with `cro` (paywalls + signup CTAs) and `documents` (invoices/statements).
---

# pricing (Binexus)

Pricing strategy + packaging for Binexus. Both the **public-facing pricing page** (`apps/web/src/app/(public)/pricing/page.tsx` when it ships) and the **internal billing schema** that lands in F7 Billing must agree. This skill defines that agreement.

Adapted from `marketingskills-main/skills/pricing` (full reference at [`skills/marketingskills-main/skills/pricing/SKILL.md`](../../../skills/marketingskills-main/skills/pricing/SKILL.md)).

## When to invoke

- Designing or revising the landing's `/pricing` page.
- Choosing the value metric for a tier (per seat, per route, per delivery, per tenant, per branch).
- Wiring Stripe products / prices for F7. Use this skill BEFORE writing Stripe IDs — they're hard to rename later.
- Adding a free trial, freemium tier, or promo.
- Picking annual vs monthly billing surfaces.
- Designing the upgrade / downgrade flow.

## Audience model (carry over from `ui-ux-pro`)

| Persona                    | What they actually pay for                             | Likely tier        |
| -------------------------- | ------------------------------------------------------ | ------------------ |
| Ops manager (solo / small) | Saves 10+ hours/week of Excel + WhatsApp triage        | Starter            |
| Small fleet owner          | Fewer late deliveries, fewer "where is my order" calls | Growth             |
| Enterprise eval            | Multi-tenant + SSO + audit + integrations + custom SLA | Scale / Enterprise |

Three tiers maximum. A fourth "Enterprise" with "Contact us" is OK and does not count.

## Value metric selection

The value metric is the single number that scales the customer's bill with the value they extract. For Binexus the candidates:

| Metric                   | Pros                                             | Cons                                                                        |
| ------------------------ | ------------------------------------------------ | --------------------------------------------------------------------------- |
| Per seat (user)          | Predictable. Easy to communicate.                | Doesn't capture value (1 dispatcher can run 100 routes). Punishes adoption. |
| Per route/day            | Aligns with actual work done.                    | Volatile. Hard to budget.                                                   |
| Per delivery stop        | Tracks output. Easy to compare against carriers. | Encourages route under-counting. Hard to audit.                             |
| Per branch               | Matches the org structure many tenants use.      | Doesn't scale with usage inside the branch.                                 |
| Per tenant + tier limits | Simplest. Each tier has caps.                    | Customers hate caps but tolerate them when tiers map to "stages".           |

**Recommended starting model for Binexus**: **per tenant + caps that grow per tier** (Starter / Growth / Scale). Caps are: branches, users, orders/month, routes/day. Add **per-delivery overage** for tenants that blow past the cap in a given month.

Re-evaluate annually. Migration from one metric to another is painful — only do it when you have data, not opinions.

## Tier template (Binexus)

```
┌─────────────┬───────────────┬───────────────┬────────────────┬───────────────┐
│             │ Free trial    │ Starter       │ Growth         │ Scale         │
├─────────────┼───────────────┼───────────────┼────────────────┼───────────────┤
│ Branches    │ 1             │ 1             │ 3              │ Unlimited     │
│ Users       │ 3             │ 5             │ 15             │ Unlimited     │
│ Orders/mo   │ 200           │ 1,000         │ 5,000          │ Custom        │
│ Routes/day  │ 5             │ 20            │ 100            │ Custom        │
│ Drivers     │ —             │ 3             │ 15             │ Unlimited     │
│ Integrations│ —             │ Email + Stripe│ + SAT/CFDI     │ + custom API   │
│ Multi-tenant│ —             │ —             │ —              │ Yes           │
│ Support     │ Community     │ Email         │ Email + chat   │ Dedicated CSM │
│ SLA         │ —             │ —             │ 99.5 %         │ 99.9 %        │
│ Price (mo)  │ Free 14 days  │ $X            │ $Y             │ Custom        │
└─────────────┴───────────────┴───────────────┴────────────────┴───────────────┘
```

Rules:

- The trial is **time-bound**, NOT feature-bound. Trial users see Growth-level features and full UI. After 14 days they pick a tier or convert to read-only.
- Each cap is **soft** at 80% (in-app warning) and **hard** at 110% (block new orders, allow overage on the next invoice with explicit consent).
- The "+" sign on integrations means strictly additive — no removed features in higher tiers.
- Annual billing: 2 months free (~17% discount). Show monthly equivalent next to annual price.

## Stripe / F7 wiring (when the time comes)

Use Stripe's `Product` + `Price` model. One Product per tier. One Price per (tier × interval × currency).

```
Stripe Product: binexus_starter
  Price: starter_monthly_mxn      (recurring monthly, MXN)
  Price: starter_yearly_mxn       (recurring yearly, MXN, 17% off)
  Price: starter_monthly_usd
  Price: starter_yearly_usd

Stripe Product: binexus_growth
  ...

Stripe Product: binexus_scale
  ...

Stripe Product: binexus_overage
  Price: per_delivery_overage_mxn (metered)
  ...
```

Migrating tiers later requires running both old + new products in parallel for legacy customers — never delete a `Product` that has active subscriptions. See [`stripe-best-practices`](../../../mcps/plugin-stripe-stripe) (already a plugin) for handling.

## Anti-patterns

- **Per-seat with no caps and no tiers.** Encourages org-wide sharing of one login; you lose all multi-user data.
- **Free forever tier.** Too much support load for a B2B operator tool that is not a lead magnet. Use a generous trial instead.
- **More than 3 tiers + Enterprise.** Decision paralysis. Cut.
- **Different "core" features in different tiers** ("Starter has orders, Growth has logistics"). Tiers gate quantity and integrations, not the core workflow.
- **Discount tactics on the landing as the default state** ("50% off!" headline). Reserved for explicit launch windows.
- **Pricing in USD only.** Binexus is LATAM-first. Show MXN / COP / ARS / etc. in addition to USD.
- **Hiding the price.** "Contact sales" only on the Enterprise tier. Starter and Growth must show a number.

## How pricing changes flow through the codebase

1. Update tier definitions in `apps/backend/src/contexts/billing/domain/tier.ts` (file to be created in F7).
2. Update Stripe products via Stripe MCP (already configured).
3. Update the public pricing page in `apps/web/src/app/(public)/pricing/page.tsx`.
4. Update `docs/domains/billing.md`.
5. Sync Notion `Billing` page.
6. Run `pnpm exec turbo run typecheck lint build` from the root.

Each step has its own PR or a single PR with all five sections clearly delineated.

## Pre-PR checklist

- [ ] Three visible tiers + optional Enterprise.
- [ ] One value metric chosen and justified in PR body.
- [ ] Soft (80%) + hard (110%) cap behaviour defined.
- [ ] Annual discount shown next to monthly.
- [ ] LATAM currency shown (at minimum MXN; ideally MXN + USD).
- [ ] Stripe `Product` + `Price` IDs documented in `docs/domains/billing.md`.
- [ ] Migration path for legacy customers if tiers changed.

## Reference

- Full upstream: [`skills/marketingskills-main/skills/pricing/SKILL.md`](../../../skills/marketingskills-main/skills/pricing/SKILL.md)
- Stripe plugin (already configured): `mcps/plugin-stripe-stripe`
- [`.cursor/skills/cro/SKILL.md`](../cro/SKILL.md) — landing CTA + signup conversion
- [`.cursor/skills/documents/SKILL.md`](../documents/SKILL.md) — invoice generation
- [`docs/domains/billing.md`](../../../docs/domains/billing.md) (placeholder until F7)
