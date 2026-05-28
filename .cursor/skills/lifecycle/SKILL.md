---
name: lifecycle
description: Lifecycle marketing for Binexus — transactional + drip emails, in-product onboarding nudges, referrals, churn prevention, launch programs, community building. Use when designing the post-signup email sequence, when planning a feature launch, when reviewing churn cohorts, when wiring referral incentives, when adding community-driven growth (user forum, customer Slack, events), or when an "Empty week 2" cohort needs a re-engagement program. Pairs with `analytics` (cohorts), `cro` (signup), `marketing-copy` (email text).
---

# lifecycle (Binexus)

Lifecycle marketing across the whole tenant journey: signup → activated → habituated → expanded → renewed → (sometimes) churned-and-recovered. Adapted from `emails`, `onboarding`, `referrals`, `launch`, `churn-prevention`, `co-marketing`, `community-marketing` (see [`skills/marketingskills-main/skills/emails/SKILL.md`](../../../skills/marketingskills-main/skills/emails/SKILL.md), [`onboarding`](../../../skills/marketingskills-main/skills/onboarding/SKILL.md), [`referrals`](../../../skills/marketingskills-main/skills/referrals/SKILL.md), [`launch`](../../../skills/marketingskills-main/skills/launch/SKILL.md), [`churn-prevention`](../../../skills/marketingskills-main/skills/churn-prevention/SKILL.md), [`co-marketing`](../../../skills/marketingskills-main/skills/co-marketing/SKILL.md), [`community-marketing`](../../../skills/marketingskills-main/skills/community-marketing/SKILL.md)).

## When to invoke

- Designing the welcome / trial / onboarding email sequence.
- Wiring the activation-nudge in-app messages.
- Adding a referral program.
- Reviewing churn — which cohorts churn, when, and why.
- Planning a public feature launch (Product Hunt, social, partner co-marketing).
- Designing the customer Slack / forum / events.

## The lifecycle (Binexus map)

| Stage      | Definition                                                                               | Owner skill                                                                            |
| ---------- | ---------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Acquired   | Visited landing, no event yet                                                            | [`growth-seo`](../growth-seo/SKILL.md), [`marketing-copy`](../marketing-copy/SKILL.md) |
| Signed up  | `signup_completed` (tenant exists, user exists, not yet activated)                       | [`cro`](../cro/SKILL.md)                                                               |
| Activated  | `tenant_activated` (first delivery confirmed — see [`analytics`](../analytics/SKILL.md)) | this skill                                                                             |
| Habituated | ≥3 deliveries confirmed in week 1; ≥1 active user week 2                                 | this skill                                                                             |
| Expanded   | Upgraded tier OR added a branch OR added drivers                                         | this skill + [`pricing`](../pricing/SKILL.md)                                          |
| Renewed    | Annual renewal OR continuous monthly retention ≥ 3 months                                | this skill                                                                             |
| At-risk    | DAU dropped vs 4-week baseline OR cap usage dropped 30%                                  | this skill                                                                             |
| Churned    | Cancelled                                                                                | this skill                                                                             |
| Recovered  | Re-signed within 6 months                                                                | this skill                                                                             |

## Trial / onboarding email sequence

Day-keyed, sent only if the user has NOT already completed the corresponding action. Honour the "stop on success" rule — never send "complete onboarding step 1" to someone who completed it 2 minutes ago.

| Day | Trigger                       | Subject (ES MX)                            | Body essence                                                    |
| --- | ----------------------------- | ------------------------------------------ | --------------------------------------------------------------- |
| 0   | `signup_completed`            | "Bienvenido a Binexus, {tenantName}"       | One screenshot + 1 link to wizard step 1.                       |
| 1   | wizard step 1 not done        | "Crea tu primera sucursal en 30 segundos"  | Two lines + button. Link to wizard step 1.                      |
| 3   | first order not created       | "Tu primer pedido está a un clic"          | Offer "Usar datos de ejemplo" button.                           |
| 5   | first order created, no route | "Despacha tu primera ruta hoy"             | Link to /logistics + 1 GIF of dispatch.                         |
| 7   | activated (T-7 to trial end)  | "Tu prueba expira en 7 días"               | Show usage so far + 2 testimonials + pricing link.              |
| 13  | activated, trial day 13 (T-1) | "Mañana expira tu prueba"                  | Show usage + upgrade link.                                      |
| 14  | trial ended, not converted    | "¿Qué te detuvo?" reply-back               | Personal reply — comes from a human address, asks one question. |
| 30  | not converted, 30d after end  | "Si vuelves, te configuro tu primera ruta" | Last touch, one-time.                                           |

All emails:

- Send domain: `mail.binexus.com` (not the primary `binexus.com`).
- Reply-to: `hola@binexus.com` (monitored, not `noreply@`).
- Text + HTML versions.
- Unsubscribe link mandatory; honour immediately.
- Translated per tenant locale, not just the user's browser.

## In-product nudges

Triggered by `tenant_state` not by time:

- Tenant created branch but no drivers in 24 h → tooltip on "Conductores" pointing to "Add your first driver".
- Tenant has orders pending picking ≥ 48 h → inline banner on `/inventory`.
- Tenant has confirmed delivery but no proof captured → tooltip on next confirm flow.

Frequency cap: max 2 in-product nudges per session. Stop showing any nudge that's been dismissed 3 times.

## Referrals (consider after activation cohort stabilizes)

Default model when ready:

- Referrer gets one month free on their current tier when the referee converts to paid.
- Referee gets 14-day extended trial (28 vs 14).
- Referrer's referral code is unique per tenant (`acme-dist-3hk`).
- Cap at 6 referral credits per tenant per year.

Mechanics:

- Trackable URL: `https://binexus.com/r/<code>` → `?ref=<code>` cookie → `signup_completed` with `referrerTenantId`.
- Credit issued at `tenant_first_paid` (not at signup — prevents fraud).
- Referrer credited via Stripe coupon, not via internal balance.

Wait to ship referrals until: (1) >50 paying tenants, (2) <10 % churn at month 3, (3) at least 5 organic referrals already happened without an explicit program. Otherwise the program ships to nobody.

## Launches

A launch is a discrete event with a defined audience and a single CTA. Binexus launch types:

| Launch                                     | When                                                   | Channels                                            | KPI                                    |
| ------------------------------------------ | ------------------------------------------------------ | --------------------------------------------------- | -------------------------------------- |
| Public launch (Product Hunt, IndieHackers) | Once. After landing + signup are polished, NOT before. | PH, IH, LinkedIn, Twitter, partner co-marketing.    | Signups on launch day; expect 200-500. |
| Feature launch                             | Each new context goes live (F4 done, F5 done...).      | Changelog page + email to active tenants + Twitter. | Adoption % within 14 days.             |
| Regional launch                            | When the platform localizes to a new geo.              | Local press + partner network + targeted ads.       | Signups from that geo.                 |

Launches need a 7-day prep checklist (assets, copy variants, press list, customer references). Use [`marketing-copy`](../marketing-copy/SKILL.md) for the text and [`taste`](../taste/SKILL.md) for assets.

## Co-marketing

Find one non-competing partner per quarter where the audience overlaps:

- **Stripe (payments)** — Binexus's payments live on Stripe; Stripe LATAM has a partner program.
- **MinIO / Cloudflare** — infra partners; technical case study.
- **Local logistics chambers** — gives credibility.
- **Local accounting / ERP integrators** — channel-grade.

Co-marketing artifacts:

- Joint webinar or recorded session.
- Joint case study.
- Joint blog post (cross-published with rel=canonical to whichever site is the primary).
- Joint promo (e.g. extended trial for partner's audience).

## Community marketing

The lightest version that fits Binexus's stage:

- A customer Slack (or Discord) with a single channel for product questions.
- A monthly "Office hours" call — 30 min on a fixed weekday.
- A public changelog (`apps/web/src/app/(public)/changelog`) — one entry per slice ship.

Skip:

- A full forum. Too much moderation overhead at our size.
- A Discord-as-everything-channel. Slack is fine.
- A "user group" event series before there are 50+ active tenants.

## Churn prevention

### Predictors (build into [`analytics`](../analytics/SKILL.md) cohorts)

1. **No primary action in 7 days** — `delivery_confirmed` count = 0 across the tenant.
2. **Cap usage dropped 30 % vs 4-week baseline** — the tenant is using us less.
3. **Driver count dropped** — they're laying off or replacing the tool.
4. **Support ticket open > 5 days** — frustration accumulates.

### Interventions in order

1. In-product nudge: "Te notamos menos activo esta semana. ¿Hay algo que podamos hacer?"
2. Founder-sent email: short, personal, asks one question. NOT a survey.
3. Phone / call from the AE.
4. Save offer (1 month free) — last resort.

### Exit interview when they churn

Ask 3 questions, max:

1. What was the trigger to cancel?
2. What would have changed your decision?
3. Would you consider coming back if X were true?

Capture in `docs/research/churn-stories.md`. Patterns feed [`customer-research`](../customer-research/SKILL.md).

## Anti-patterns

- A 14-email "drip" with no stop-on-success rule. Spam.
- "Welcome to the family!" email. Sounds bro / corporate.
- Referrals before retention is healthy. You'll be referring leakage.
- Launching to dead silence — Product Hunt without warmup network is dead silence.
- Slack community with 200 users and 4 messages per week. Looks worse than not having one.
- "Re-engagement" emails sent to the entire dormant list at once. Triggers spam flagging.

## Pre-PR checklist

- [ ] All emails honour "stop on success".
- [ ] Frequency cap defined and enforced.
- [ ] Reply-to is monitored.
- [ ] Translated per tenant locale.
- [ ] Triggers wired into [`analytics`](../analytics/SKILL.md) cohorts, not raw cron.
- [ ] Unsubscribe respected within 1 minute of click.
- [ ] Each email has a single CTA.
- [ ] If launch: 7-day prep checklist completed.

## Reference

- [`skills/marketingskills-main/skills/emails/SKILL.md`](../../../skills/marketingskills-main/skills/emails/SKILL.md)
- [`skills/marketingskills-main/skills/onboarding/SKILL.md`](../../../skills/marketingskills-main/skills/onboarding/SKILL.md)
- [`skills/marketingskills-main/skills/referrals/SKILL.md`](../../../skills/marketingskills-main/skills/referrals/SKILL.md)
- [`skills/marketingskills-main/skills/launch/SKILL.md`](../../../skills/marketingskills-main/skills/launch/SKILL.md)
- [`skills/marketingskills-main/skills/churn-prevention/SKILL.md`](../../../skills/marketingskills-main/skills/churn-prevention/SKILL.md)
- [`skills/marketingskills-main/skills/co-marketing/SKILL.md`](../../../skills/marketingskills-main/skills/co-marketing/SKILL.md)
- [`skills/marketingskills-main/skills/community-marketing/SKILL.md`](../../../skills/marketingskills-main/skills/community-marketing/SKILL.md)
- [`.cursor/skills/analytics/SKILL.md`](../analytics/SKILL.md)
- [`.cursor/skills/cro/SKILL.md`](../cro/SKILL.md)
- [`.cursor/skills/marketing-copy/SKILL.md`](../marketing-copy/SKILL.md)
