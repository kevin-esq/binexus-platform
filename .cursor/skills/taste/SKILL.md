---
name: taste
description: Anti-slop frontend taste for the Binexus public landing, free-trial signup, and onboarding pages — NOT for the operator panel under `/orders /logistics /inventory /warehouse`. Use when building or redesigning any page meant for prospects or first-time tenants: marketing pages, pricing, signup, onboarding wizard, public docs. Reads the brief (B2B logistics SaaS, multi-tenant), sets dials (variance / motion / density), and rejects the LLM defaults (AI purple, generic glassmorphism, identical card grids, centered hero over dark mesh, Inter + slate-900).
---

# taste (Binexus)

Anti-slop frontend taste for the **public surface** of Binexus — landing, pricing, free-trial signup, onboarding, public docs. Pairs with `ui-ux-pro`. Does NOT apply to the operator panel (use `react-best-practices` + `composition-patterns`).

Adapted from `taste-skill-main/skills/taste-skill`. The original is ~1200 lines and worth reading end-to-end when designing a new public page: [`skills/taste-skill-main/skills/taste-skill/SKILL.md`](../../../skills/taste-skill-main/skills/taste-skill/SKILL.md). This skill is the tight loop for Binexus.

## Where this applies

- `apps/web/src/app/(public)/**` — landing, pricing, signup, login, onboarding wizard (paths will be added as the landing ships).
- Any marketing route under `app/(marketing)`, `app/landing/`, etc.
- The trial-signup flow that creates a tenant.
- Public docs / status / changelog pages.

NOT for: `/orders`, `/logistics`, `/inventory`, `/warehouse`, `/dispatch`. Those are operator dashboards — covered by [`react-best-practices`](../react-best-practices/SKILL.md) and the platform's data-density rules.

## 0. Read the brief before touching code

Before any layout, state in one line: **"Reading this as: B2B logistics SaaS landing for `<audience>`, with `<vibe>` language, leaning toward `<aesthetic family>`."**

Default Binexus read:

> Reading this as: B2B SaaS landing for ops / logistics managers + small fleet owners (LATAM, ES-first), with a serious-but-modern technical language, leaning toward Tailwind v4 utilities + Geist + restrained motion + one committed accent.

Adjust only when there's an explicit override (the user references a brand, a screenshot, a competitor like Onfleet / Bringg / Routific / Locus).

## 1. Dials (Binexus baseline)

- `DESIGN_VARIANCE: 6` — clean but not flat. Some asymmetry in hero. Not Awwwards.
- `MOTION_INTENSITY: 4` — restrained. Hero reveal, scroll-driven section transitions, hover micro-interactions. No bouncy springs, no cinematic intros.
- `VISUAL_DENSITY: 3` — air. Operators are tired; the landing breathes. Density lives in the demo screenshots, not in the marketing copy.

Override these only when the user explicitly says "más Awwwards", "más editorial", "más minimal", etc.

## 2. Stack defaults for the Binexus landing

- **Framework**: Next.js App Router (already in `apps/web`). Public pages are RSC by default; isolate motion / scroll listeners into `'use client'` leaves.
- **Styling**: Tailwind v4 utilities. No CSS-in-JS. No design-system import (no MUI / Chakra / Mantine).
- **Animation**: `motion` (formerly `framer-motion`) — `import { motion } from 'motion/react'`. Use `useMotionValue` / `useScroll` for continuous values, never `useState`.
- **Fonts**: Geist + Geist Mono via `next/font/local` or `next/font/google`. Never link Google Fonts via `<link>`.
- **Icons**: `@phosphor-icons/react` as the only icon family for the public surface. Pin `weight="duotone"` or `weight="regular"` consistently per project. Never mix icon libraries.

## 3. Absolute bans (match-and-refuse)

Never ship any of these in the landing. If you catch yourself about to write one, restructure.

- **AI-purple gradients.** No `from-purple-500 via-blue-500 to-pink-500`. Pick a single committed accent and stick to it.
- **Centered hero over dark mesh / radial gradient.** The LLM default. Use asymmetric hero, real product screenshot, or a brand-coloured solid surface.
- **Three equal feature cards with icon + heading + body.** SaaS cliché. Use one anchor case + two supporting variants, or a long-form editorial block, or a real product screenshot grid.
- **Glassmorphism as default.** `backdrop-filter` decoratively on everything. Allowed only when it serves a specific layered scene (e.g. floating navbar over a photo).
- **Gradient text.** `background-clip: text` over a rainbow gradient. Solid color, weight or size for emphasis.
- **Side-stripe borders.** `border-l-4` colored stripes on cards. Use full borders, tinted backgrounds, or nothing.
- **Hero-metric template.** Big number / small label / supporting stats / gradient accent. The exact stat-card-row most SaaS sites use.
- **Inter + slate-900.** The single most-saturated combo. Default to Geist + a tinted neutral. Inter is acceptable ONLY when the user explicitly asks for a Linear / GOV-style register.
- **Em dashes.** Use commas, colons, semicolons, periods, or parentheses. Never `--` either.
- **Fraunces / Instrument Serif** as the display serif. The two most-saturated AI-favorite display serifs. If a serif is genuinely justified, pick from the rotation in the full skill (PP Editorial New, Reckless Neue, Migra, etc.).
- **Warm-beige + brass + espresso "premium consumer" palette** (`#f5f1ea`, `#b08947`, `#1a1714` and neighbours). Binexus is logistics, not artisan goods.

## 4. Color strategy

Default to **Committed**: one saturated color carries 30–60% of the surface, neutrals are tinted toward that hue. Pick one of:

- **Cold technical**: graphite + electric blue accent.
- **Industrial warm**: charcoal + burnt orange accent.
- **Operator green**: zinc + emerald accent (think dispatch screens).

OKLCH for every color. Reduce chroma as lightness approaches 0 or 100. Never `#000` or `#fff` — tint every neutral toward the brand hue (chroma 0.005–0.01).

Only one accent across the page. If section 7 needs another color, rework the section.

## 5. Layout

- Container: `max-w-[1400px] mx-auto` or `max-w-7xl`. Pick once, stick to it.
- Full-height hero: `min-h-[100dvh]`, never `h-screen`. iOS Safari address bar will jump.
- Multi-column grids: CSS Grid (`grid grid-cols-1 md:grid-cols-3 gap-6`). Never `calc(33%-1rem)` flex math.
- Vary spacing for rhythm. Section padding should not be uniform across the page.
- Cards are the lazy answer. Use them only when the content is genuinely a card. Nested cards are always wrong.

## 6. Copy (with `stop-slop`)

- Every word earns its place. See [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md).
- Hero headline: one phrase, plain language, names the outcome ("Dispatcha 10× rutas sin pelearte con la hoja de cálculo"). No "Empower your logistics with AI-powered orchestration".
- Bilingual: the primary landing is ES-first (LATAM). Copy is human-translated when EN ships, not auto-translated.

## 7. Conversion surfaces

Three CTAs maximum across the whole landing:

1. **Free trial / signup** — the primary one. Lives in hero + repeated near-bottom. Same wording everywhere ("Empieza prueba gratis" or equivalent).
2. **Book a demo** — for fleet >50 vehicles or enterprise paths.
3. **Login** — top-right, visually muted.

Signup flow:

- Single-page form when possible (email + password + tenant name).
- Confirms with `tenantId` returned by the backend.
- Sends user to the onboarding wizard (separate skill: see `ui-ux-pro`).

## 8. Pre-ship checklist

Before merging a public page:

- [ ] Design read stated in one line in the PR body.
- [ ] No item from the "Absolute bans" list shipped.
- [ ] One accent color, locked, used everywhere.
- [ ] One icon family.
- [ ] `min-h-[100dvh]` on full-height heroes.
- [ ] No `h-screen`.
- [ ] No em dashes in any copy.
- [ ] All copy passed through `stop-slop`.
- [ ] Lighthouse a11y ≥ 95 on the new route.
- [ ] No imports from `@prisma/client` (web app cannot touch the DB).
- [ ] `pnpm exec turbo run typecheck lint build --filter=@binexus/web` green.

## Reference

- Full taste manifesto: [`skills/taste-skill-main/skills/taste-skill/SKILL.md`](../../../skills/taste-skill-main/skills/taste-skill/SKILL.md)
- [`.cursor/skills/ui-ux-pro/SKILL.md`](../ui-ux-pro/SKILL.md) — premium UX patterns for onboarding + signup
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md) — copy hygiene
- [`.cursor/skills/react-best-practices/SKILL.md`](../react-best-practices/SKILL.md) — RSC / Client boundary, bundle size
- [`.cursor/skills/react-view-transitions/SKILL.md`](../react-view-transitions/SKILL.md) — navigation animations between landing sections
