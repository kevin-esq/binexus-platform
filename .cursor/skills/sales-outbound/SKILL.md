---
name: sales-outbound
description: F5 Sales outbound playbook for Binexus — prospecting, cold email, sales enablement, RevOps wiring. Use when writing first-touch sequences to ops managers / fleet owners, building the lead-list approach, defining the SDR/AE handoff, or wiring CRM + pipeline reporting. Pairs with `customer-research` (ICP discovery), `marketing-copy` (subject lines), `pricing` (offer), and `lifecycle` (post-signup nurture).
---

# sales-outbound (Binexus)

Outbound sales motion for the F5 Sales phase. Adapted from the merged upstream skills `cold-email`, `prospecting`, `sales-enablement`, `revops` (see [`skills/marketingskills-main/skills/cold-email/SKILL.md`](../../../skills/marketingskills-main/skills/cold-email/SKILL.md), [`prospecting`](../../../skills/marketingskills-main/skills/prospecting/SKILL.md), [`sales-enablement`](../../../skills/marketingskills-main/skills/sales-enablement/SKILL.md), [`revops`](../../../skills/marketingskills-main/skills/revops/SKILL.md)).

## When to invoke

- Drafting the first 100-prospect outbound batch.
- Designing the SDR/AE handoff: when does an inbound MQL become an SQL.
- Wiring the CRM (HubSpot / Pipedrive / Salesforce — TBD) so it talks to the backend tenant data.
- Building the pipeline reporting that F8 Reporting will need.
- Reviewing a stale outbound sequence that converts at <1%.

## ICP (build with `customer-research`)

Don't guess. The ICP is downstream of [`customer-research`](../customer-research/SKILL.md). Default starting hypothesis for Binexus, to be validated:

- **Geo**: México (CDMX, Monterrey, Guadalajara), Colombia (Bogotá, Medellín), Argentina (Buenos Aires).
- **Industry**: Last-mile distribution (food, e-commerce fulfilment, B2B distribution).
- **Fleet size**: 5-50 vehicles. Below 5 they tolerate Excel; above 50 they have internal tooling or a TMS.
- **Trigger**: a public signal that the company is scaling — hiring drivers, opening a new warehouse, switching from a carrier to in-house delivery, mentions of "Excel" pain in LinkedIn / Twitter.

## Prospecting

### Channels in order of yield (per upstream + general B2B priors)

1. **LinkedIn Sales Navigator** — exact-fit firmographic + intent filters. Build a saved search per geo + size + role.
2. **Apollo / Hunter / Clay** — email enrichment for the LinkedIn-sourced contacts.
3. **Public ops directories** (Vamos, Vince Co., national logistics chambers) — surfaces "logistics manager" type roles that LinkedIn misses for SMB.
4. **WhatsApp groups + local meetups** — high-trust, low-volume.
5. **Cold call** — last resort.

### Lead-list discipline

- One `Prospect` row = one human. Never a "company" row.
- Required fields: full name, role, company, country/city, fleet-size estimate, public trigger, source URL.
- Sourced from at least 2 systems independently (LinkedIn + Apollo) before any outreach.
- Verified email syntax + domain MX records before send (low bounce → high deliverability).

## Cold email

### Anatomy of the first touch

| Section   | Length      | Rule                                                                          |
| --------- | ----------- | ----------------------------------------------------------------------------- |
| Subject   | 3-7 words   | Curiosity, not promise. No emojis. No "RE:" trick.                            |
| Preheader | 50-80 chars | One line that earns the open.                                                 |
| Opener    | 1 sentence  | Personal observation from public signal — never "I came across your profile". |
| Pain      | 1 sentence  | Name the specific pain. Use their vocabulary. ES if the prospect is ES-first. |
| Bridge    | 1 sentence  | One concrete outcome other operators got with Binexus. Use a number.          |
| Ask       | 1 sentence  | A 15-min slot. NEVER "let me know if interested".                             |
| Sig       | Minimal     | Name + role + reply-to.                                                       |

Total: under 90 words. Read-through time ≤ 25 seconds.

### Sequence

| Day | Touch                                                                         |
| --- | ----------------------------------------------------------------------------- |
| 0   | First email (above).                                                          |
| 3   | Reply-to-same-thread: short, single question.                                 |
| 7   | Different angle — case study (when we have one) or a relevant product detail. |
| 14  | Break-up email: "should I close the loop?"                                    |
| —   | Stop. Mark prospect as `nurture-only` for 90 days.                            |

Max 4 emails in the sequence. Five+ touches do not move the needle and hurt deliverability.

### Deliverability hygiene

- Send from a warm domain (`outbound.binexus.com`, not the primary `binexus.com`).
- SPF + DKIM + DMARC set up before the first send.
- Bounce rate target: <2 %. Above 4 % stop the sequence immediately.
- Reply rate target: 8 % on the first batch; iterate down from there.
- Never buy lists. Build per `prospecting` discipline above.
- Don't link a tracking pixel in cold-1; the open-rate signal isn't worth the deliverability cost.

## Sales enablement

### Materials to have day one

- **One-pager** (PDF, 1 page, bilingual ES/EN) — what Binexus does, three outcomes, pricing tiers from `pricing`, CTA to demo.
- **Demo script** (15-min) — fixed seed tenant + scripted flow: create order → reserve inventory → dispatch route → confirm delivery with proof.
- **Pricing sheet** — same data as the landing's `/pricing` page. Single source: when one changes, both change.
- **Objection map** — for the five most-common objections (price, lock-in, multi-tenant trust, integrations, SLA). Each has a 30-word counter.
- **Reference customers** — three named tenants with logo + one-sentence testimonial, BEFORE we use them in outbound. Get written consent.

### SDR ↔ AE handoff

- Pass: prospect agreed to a 30-min call AND fleet ≥5 vehicles AND there's a named decision-maker.
- Fail: pass too soon (no fleet info) or too late (AE never engaged). Both kill velocity.
- Handoff doc: see [`docs/sales/handoff-template.md`](../../../docs/sales/handoff-template.md) (create in F5 kickoff).

## RevOps

### CRM data model (mirror the Binexus domain)

- `Lead` — pre-qualification.
- `Account` — eventually maps to a Binexus `Tenant`. When the customer signs up, `Account.tenantId` is set and the CRM row hyperlinks to the operator panel.
- `Opportunity` — one per active deal.
- `Contact` — humans inside an account.

The mapping is bidirectional: Binexus webhooks the CRM on tenant lifecycle events (`TenantCreated`, `TenantUpgraded`, `TenantChurned`). When F7 ships, the `Tenant` domain emits these.

### Pipeline reporting (preview of F8)

- **Stages**: Prospect → Contacted → Qualified → Demo Scheduled → Demo Done → Proposal → Closed Won / Closed Lost.
- **Velocity**: median days at each stage. Above 45 days at one stage = stuck deal.
- **Win rate**: closed won / (closed won + closed lost). Target ≥30 % after the first 50 closed deals.
- **CAC**: monthly outbound spend / closed-won. Watch this from week 1.

## Anti-patterns

- "Spray and pray" — 1,000 generic emails. Burns the domain.
- "Hi {{first_name}}" with no real personalization. Worse than no name at all when it breaks.
- A demo before qualifying fleet size — wastes both parties.
- AE follow-up after the SDR handoff in a _new_ thread. Always reply in-thread to preserve context.
- CRM stages with no exit criteria. Velocity dies.
- Sending in MXN to an Argentine prospect (wrong currency on the proposal).
- Doing outbound before pricing is published (the prospect asks for pricing and you lose the deal in the call).

## Pre-PR / pre-send checklist

- [ ] ICP signal explicit in the email body, not just the subject.
- [ ] Pricing referenced (or "starts at $X") so the prospect knows the order of magnitude.
- [ ] Sequence has ≤4 touches.
- [ ] Email passes `stop-slop` (no em dashes, no "I hope this email finds you well").
- [ ] Bounce rate from the warm-up batch <2 %.
- [ ] CRM stage definitions written down before the first 10 prospects move through.

## Reference

- [`skills/marketingskills-main/skills/cold-email/SKILL.md`](../../../skills/marketingskills-main/skills/cold-email/SKILL.md)
- [`skills/marketingskills-main/skills/prospecting/SKILL.md`](../../../skills/marketingskills-main/skills/prospecting/SKILL.md)
- [`skills/marketingskills-main/skills/sales-enablement/SKILL.md`](../../../skills/marketingskills-main/skills/sales-enablement/SKILL.md)
- [`skills/marketingskills-main/skills/revops/SKILL.md`](../../../skills/marketingskills-main/skills/revops/SKILL.md)
- [`.cursor/skills/customer-research/SKILL.md`](../customer-research/SKILL.md) — ICP discovery
- [`.cursor/skills/marketing-copy/SKILL.md`](../marketing-copy/SKILL.md) — subject lines + body
- [`.cursor/skills/pricing/SKILL.md`](../pricing/SKILL.md) — offer
- [`.cursor/skills/lifecycle/SKILL.md`](../lifecycle/SKILL.md) — post-signup retention
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md) — copy hygiene
