---
name: customer-research
description: Discover who Binexus is for and why they would pay. Use when defining or refining the ICP for F5 Sales, before designing pricing tiers, before writing landing copy, when a feature decision needs validation, when prep'ing customer interviews, or when profiling competitors. Pairs with `sales-outbound` (ICP feeds outreach), `pricing` (research feeds value metric), `growth-seo` (search terms feed content), and `cro` (jobs-to-be-done feed CTAs).
---

# customer-research (Binexus)

Disciplined customer discovery + competitor profiling for Binexus. Adapted from the merged upstream skills `customer-research`, `competitor-profiling`, `competitors` (see [`skills/marketingskills-main/skills/customer-research/SKILL.md`](../../../skills/marketingskills-main/skills/customer-research/SKILL.md), [`skills/marketingskills-main/skills/competitor-profiling/SKILL.md`](../../../skills/marketingskills-main/skills/competitor-profiling/SKILL.md), [`skills/marketingskills-main/skills/competitors/SKILL.md`](../../../skills/marketingskills-main/skills/competitors/SKILL.md)).

## When to invoke

- Before F5 Sales kickoff — locks the ICP that `sales-outbound` will target.
- Before tier-design decisions in [`pricing`](../pricing/SKILL.md) — the value metric comes from research, not from a whiteboard.
- Before writing a landing — the headline names the prospect's job-to-be-done, not your product's category.
- When churn shows up — talk to the churned customers before guessing why.
- Before adding a feature that costs more than 1 PR — validate that someone wants it badly enough to switch.

## Three modes

### Mode 1 — Discovery interview (JTBD framework)

Goal: find the **switch story** that brought a customer (or prospect, or churned customer) to seek a solution.

15-30 min calls. Record only with consent. Transcribe.

Question backbone (in order):

1. "Cuéntame qué pasó el día que decidiste buscar una solución para [logística / despacho / órdenes]."
2. "¿Qué estabas usando antes?" (Almost always: Excel + WhatsApp + a paper notebook. Confirm.)
3. "¿Qué pasó esa semana que te hizo decir 'esto ya no'?"
4. "¿Buscaste algo? ¿Qué buscaste, exactamente?" (Capture exact search terms — these feed [`growth-seo`](../growth-seo/SKILL.md).)
5. "¿Qué probaste? ¿Por qué descartaste cada uno?"
6. "¿Quién más participó en la decisión?" (Decision unit.)
7. "Si tuvieras que volver a contratar Binexus mañana, ¿qué necesitarías ver en el sitio para decidir en 5 minutos?"

Anti-questions (never ask):

- "¿Comprarías una feature que [feature]?" — leading; people lie politely.
- "¿Cuánto pagarías por [thing]?" — meaningless without context.
- "¿Recomendarías Binexus?" — NPS, not discovery.

### Mode 2 — Competitor profile

For each direct competitor (Onfleet, Routific, Bringg, Tookan, regional LATAM TMS), capture:

- **Positioning sentence** they use on their landing (quote verbatim).
- **Pricing model** (per route, per driver, per seat, etc.) and starting price in MXN/USD.
- **Free trial / freemium**: yes/no, duration, restrictions.
- **Target geo** (the languages on their site reveal this).
- **Three features they emphasize** in the hero.
- **One feature they lack** that Binexus has (or vice versa).
- **Tenant size sweet spot** (from their case studies — fleet sizes mentioned).
- **Founding year + funding** (signal of staying power).
- **Public reviews**: G2 / Capterra ratings + the top 3 themes in complaints.

Store one row per competitor in `docs/competitors/<slug>.md`. Refresh quarterly.

### Mode 3 — Switch story diary

A lightweight async habit. Every time a prospect or customer drops a story into a call or email, capture it in `docs/research/switch-stories.md`:

```markdown
## 2026-05-20 — Acme Logistics, MX (15 vehicles)

Trigger: Driver lost a delivery sheet during route. Customer called. CEO escalated.

Old: Excel for routes, WhatsApp group with drivers.

Searched: "control de entregas Mexico", "app para repartidores".

Tried: Tookan (too expensive), local TMS (Spanish-only support but no mobile app for drivers).

Decided Binexus because: free trial + drivers can confirm with photo + already in MXN.
```

After 20+ stories the patterns become obvious. That's the ICP.

## ICP synthesis

Once you have 5+ discovery interviews + 5+ switch stories, write the ICP doc at `docs/research/icp.md`. Sections:

1. **Firmographic** — country, industry, fleet size, revenue range, age.
2. **Trigger events** — the 3-5 events that make someone start searching (use Mode 1 Q3 answers).
3. **Jobs-to-be-done** — top 3 jobs in customer vocabulary.
4. **Search vocabulary** — exact phrases customers use. Feeds [`growth-seo`](../growth-seo/SKILL.md).
5. **Decision unit** — who recommends, who decides, who blocks.
6. **Disqualifiers** — who Binexus is NOT for (e.g. fleets <5 vehicles, single-driver couriers).

Update quarterly. Treat as live, not as a snapshot.

## Storage discipline

- Transcripts: `research/transcripts/<date>-<initials>.md` (gitignored — contains PII).
- Anonymized findings: `docs/research/*.md` (in repo).
- Recordings: external bucket with explicit retention TTL. Never in the repo.
- Always anonymize before committing.

## Competitor comparisons

The [`growth-seo`](../growth-seo/SKILL.md) skill will eventually produce "Binexus vs `<competitor>`" comparison pages. Each page is driven by:

- A profile from Mode 2.
- A switch-story quote from Mode 3 about why someone chose Binexus over that competitor.
- A neutral, factual comparison table. Never disparage. The reader will trust the page only if it reads honestly.

## Anti-patterns

- Interviewing prospects after the demo — you get product feedback, not discovery.
- Letting your own product show up in the questions ("would you like a feature that…").
- Counting NPS as research.
- Profiling 30 competitors. Pick the 5 that actually show up in switch stories.
- Updating the ICP based on one loud customer.
- Treating the ICP as fixed. It evolves with each phase of Binexus.

## Pre-PR checklist (when you ship research outputs)

- [ ] Transcripts NOT committed.
- [ ] PII redacted in everything that touches `docs/research/`.
- [ ] ICP cites N stories / N interviews behind each claim.
- [ ] Competitor profiles dated. Refresh quarterly.
- [ ] Switch stories tagged with company size + geo.

## Reference

- [`skills/marketingskills-main/skills/customer-research/SKILL.md`](../../../skills/marketingskills-main/skills/customer-research/SKILL.md)
- [`skills/marketingskills-main/skills/competitor-profiling/SKILL.md`](../../../skills/marketingskills-main/skills/competitor-profiling/SKILL.md)
- [`skills/marketingskills-main/skills/competitors/SKILL.md`](../../../skills/marketingskills-main/skills/competitors/SKILL.md)
- [`.cursor/skills/sales-outbound/SKILL.md`](../sales-outbound/SKILL.md)
- [`.cursor/skills/pricing/SKILL.md`](../pricing/SKILL.md)
- [`.cursor/skills/growth-seo/SKILL.md`](../growth-seo/SKILL.md)
- [`.cursor/skills/cro/SKILL.md`](../cro/SKILL.md)
