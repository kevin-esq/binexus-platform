---
name: marketing-copy
description: Marketing copy for the Binexus landing, ads, transactional emails, in-app messaging, and any text the prospect or tenant reads outside the operator panel. Use when writing or reviewing headlines, sub-headlines, hero copy, CTA labels, ad creative (text), social posts, push notifications, and onboarding microcopy. Pairs with `taste` (visual direction), `stop-slop` (hygiene), `cro` (CTA experiments), `growth-seo` (keyword surface), `customer-research` (vocabulary source).
---

# marketing-copy (Binexus)

Marketing-grade copy for everything Binexus says to a customer or prospect. Adapted from the merged upstream skills `copywriting`, `copy-editing`, `ad-creative`, `ads`, `marketing-psychology`, `marketing-ideas` (see [`skills/marketingskills-main/skills/copywriting/SKILL.md`](../../../skills/marketingskills-main/skills/copywriting/SKILL.md), [`copy-editing`](../../../skills/marketingskills-main/skills/copy-editing/SKILL.md), [`ad-creative`](../../../skills/marketingskills-main/skills/ad-creative/SKILL.md), [`ads`](../../../skills/marketingskills-main/skills/ads/SKILL.md), [`marketing-psychology`](../../../skills/marketingskills-main/skills/marketing-psychology/SKILL.md), [`marketing-ideas`](../../../skills/marketingskills-main/skills/marketing-ideas/SKILL.md)).

Not for: operator panel UI copy (handled by [`ui-ux-pro`](../ui-ux-pro/SKILL.md) + [`stop-slop`](../stop-slop/SKILL.md)). Not for: code comments / docs (use [`stop-slop`](../stop-slop/SKILL.md)).

## When to invoke

- Drafting any page in `apps/web/src/app/(public)/**`.
- Writing the hero / pricing / feature copy.
- Writing transactional emails (welcome, password reset, invoice).
- Writing drip / nurture sequences.
- Writing ad creative (Google, Meta, LinkedIn, TikTok).
- Writing push notification text (driver app).
- Writing onboarding microcopy.
- Reviewing copy that "sounds AI-y" — first pass with [`stop-slop`](../stop-slop/SKILL.md), second pass here.

## Voice (Binexus)

| Dimension                 | Setting                                                                                                       |
| ------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Register                  | Professional, technical-friendly. Not corporate. Not bro-y.                                                   |
| Person                    | Second person ("tu equipo", "tu flota"). Never third-person ("operators get..."). Never "we" without context. |
| Tense                     | Present indicative. Future only when literal ("La próxima entrega se confirma con foto").                     |
| Language                  | ES-MX primary; EN-US when the user explicitly switches.                                                       |
| Tone toward competitor    | Neutral, factual. Never disparage. We win on specifics, not on insults.                                       |
| Tone toward customer pain | Direct, empathetic, one sentence. Never theatrical.                                                           |

Specifics — examples that pass:

> "Dispatcha 10× rutas sin pelearte con la hoja de cálculo."
> "El conductor confirma con foto + firma. Tu cliente sabe a las 2:47 PM que llegó su pedido."

Specifics — examples that fail (do NOT ship):

> "Empower your logistics operations with AI-powered orchestration." (corporate)
> "Stop wasting time on Excel — make the leap to Binexus today!" (bro)
> "We at Binexus believe in transforming the way you do logistics." (we)

## The five copy frames

For any new section / page / ad, pick ONE frame. Mixing is muddled.

### 1. Outcome frame

> "[Subject] [verb in present] [concrete outcome with a number]."

Use for: landing hero, ad headlines.

> "Las flotas en Monterrey reducen 38% sus rutas reabiertas con Binexus."

### 2. Old way / new way frame (use sparingly — flagged by `stop-slop` if overused)

Only when the comparison is essential to comprehension.

> "Antes: 6 hojas de Excel + 4 grupos de WhatsApp. Ahora: una sola pantalla."

### 3. Definitional frame

> "[Subject] is [category] for [audience] that [unique angle]."

Use for: `<head>` description, definitional sections, FAQ.

> "Binexus es el TMS para flotas pequeñas en LATAM que necesitan que sus conductores confirmen con foto, no con WhatsApp."

### 4. Question frame

> "¿[Question that customer literally asks]?"

Use for: FAQ headings, blog headings.

> "¿Cómo asigno paradas a un nuevo conductor sin reentrenar a todo el equipo?"

### 5. Story frame

A one-sentence customer story.

> "Acme Distribution dispatcha 47 rutas diarias desde una sola tableta — sin perder ni un solo punto en CFDI."

Use for: testimonial blocks, case study openers, social posts.

## Headlines — the discipline

- One job per headline.
- Specific over generic ("38% menos rutas reabiertas" beats "más eficiente").
- 5-9 words.
- Customer's vocabulary, not Binexus's. Pull words from interview transcripts.
- No subordinate clauses. No commas. No em dashes.

Test: read the headline. If you cannot say what the customer would do next in one sentence, the headline failed.

## CTAs — the discipline

- Verb-first ("Empieza prueba gratis", "Ver pricing"). Not noun ("Free trial").
- Repeat the same wording across the page. The hero CTA, the mid-page CTA, and the footer CTA say the same thing.
- Never use "Click here" / "Learn more". They are placeholders.
- Maximum 3 CTAs across the whole landing (primary trial + demo + login). The "secondary" is the demo; the "tertiary" is the muted login.

## Ads — text adaptation

| Channel          | Headlines                                                     | Description / body                                                             | Notes                                                                      |
| ---------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| Google Search    | 30-char × 15 (Performance Max needs many)                     | 90-char × 4                                                                    | A keyword from `growth-seo`; never the brand alone. Use the outcome frame. |
| Google Display   | banner + headline                                             | 80-char                                                                        | Image is the lift; copy is functional.                                     |
| Meta / Instagram | 40-char primary text + 27-char headline + 27-char description | Visual first. Copy supports. Story frame works best on Meta.                   |
| LinkedIn         | 70-char headline + 600-char post                              | Switch story frame. LinkedIn audience reads.                                   |
| TikTok / Reels   | overlay text + caption                                        | Question frame. Voice-over is the headline; on-screen text is the punctuation. |

## Transactional emails (when F7+ wires them)

Required transactional emails:

- Welcome (first login).
- Verify email.
- Password reset.
- Invoice issued.
- Payment failed.
- Trial expiring (T-7 days, T-1 day).
- Tenant invited to user.

Rules:

- Subject ≤ 50 chars. First 5 words contain the value.
- Preheader ≤ 80 chars; never repeats the subject.
- Body ≤ 120 words. One primary CTA.
- Reply-To is a monitored human address (`hola@binexus.com`), not `noreply@`.
- Multi-tenant: tenant name in the subject and the body, never just "your account".
- Plain text version included (some clients still show it).

## Microcopy (in-app messaging that the landing inherits)

The landing CTA must match the signup screen's first label. The signup screen's first label must match the wizard's first prompt. The wizard's "done" screen must match the panel's first empty state.

Continuity > cleverness.

## Anti-patterns

- "We at Binexus..."
- "Click here to learn more."
- "Lorem ipsum" left in copy past first review (it gets shipped — guaranteed).
- Generic "AI-powered" in the headline. Means nothing.
- Adjective stacking ("powerful, scalable, easy-to-use"). Pick one. Quantify.
- A different word in the hero CTA vs the mid-page CTA vs the email CTA.
- Translating EN copy literally to ES. Translation ≠ copy. Re-write per [voice](#voice-binexus).

## Pre-PR checklist

- [ ] Copy passes [`stop-slop`](../stop-slop/SKILL.md): no AI tells, no em dashes, no filler.
- [ ] Headline matches the frame chosen.
- [ ] CTA repeats consistently across the page.
- [ ] Pulled at least one specific number (% / minutes / dollars) from real data.
- [ ] If ES: read aloud by an ES-MX native, sounds natural.
- [ ] No competitor name disparaged.
- [ ] OG / email subject / page title aligned.

## Reference

- [`skills/marketingskills-main/skills/copywriting/SKILL.md`](../../../skills/marketingskills-main/skills/copywriting/SKILL.md)
- [`skills/marketingskills-main/skills/copy-editing/SKILL.md`](../../../skills/marketingskills-main/skills/copy-editing/SKILL.md)
- [`skills/marketingskills-main/skills/ad-creative/SKILL.md`](../../../skills/marketingskills-main/skills/ad-creative/SKILL.md)
- [`skills/marketingskills-main/skills/ads/SKILL.md`](../../../skills/marketingskills-main/skills/ads/SKILL.md)
- [`skills/marketingskills-main/skills/marketing-psychology/SKILL.md`](../../../skills/marketingskills-main/skills/marketing-psychology/SKILL.md)
- [`skills/marketingskills-main/skills/marketing-ideas/SKILL.md`](../../../skills/marketingskills-main/skills/marketing-ideas/SKILL.md)
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md) — hygiene pass
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md) — visual direction
- [`.cursor/skills/cro/SKILL.md`](../cro/SKILL.md) — CTA experiments
- [`.cursor/skills/growth-seo/SKILL.md`](../growth-seo/SKILL.md) — keyword surface
- [`.cursor/skills/customer-research/SKILL.md`](../customer-research/SKILL.md) — vocabulary source
