---
name: impeccable
description: Production-grade visual audit and design iteration for any Binexus frontend surface. Use when the user says "design", "redesign", "shape", "critique", "audit", "polish", "clarify", "distill", "harden", "optimize", "adapt", "animate", "colorize", "extract", or "improve" any UI — landing, signup, onboarding, dashboard, panel, component. Loads `PRODUCT.md` and `DESIGN.md` context, runs the matching sub-command (`shape`, `critique`, `audit`, `polish`, `bolder`, `quieter`, `delight`, `optimize`, `live`), and ships working code with anti-pattern detection. Not for backend-only or non-UI tasks.
---

# impeccable (Binexus)

Visual-design power-tool for Binexus frontend work. Designs, redesigns, critiques, polishes, hardens, and audits production UI. Real working code, committed design choices, anti-pattern detection.

Adapted from `impeccable` v2.1.9 (Apache 2.0, npm: `impeccable`). The full instruction set is enormous — this skill is the Binexus-tailored entry point. The full reference (commands, dials, design laws, register-specific rules, sub-command pages) lives in the upstream package and can be invoked via `npx impeccable <command>`.

## When to use

The user mentions any of: design, redesign, shape, critique, audit, polish, clarify, distill, harden, optimize, adapt, animate, colorize, extract, improve, bolder, quieter, delight, layout, typeset, onboard, overdrive, live.

If the user did NOT mention a sub-command, treat the request as a general design invocation and apply the [Shared design laws](#shared-design-laws) below.

If they did mention a sub-command (e.g. "polish the signup page"), invoke the matching command — load the upstream reference for that command before designing.

## Setup (always run first)

### 1. Context gathering

Binexus has its own context files. They map onto impeccable's expected `PRODUCT.md` / `DESIGN.md`:

| Impeccable expects | Binexus equivalent                                                                                                            |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| `PRODUCT.md`       | [`docs/PRODUCT.md`](../../../docs/PRODUCT.md) (create if missing — Users, Brand, Tone, Anti-references, Strategic principles) |
| `DESIGN.md`        | [`docs/DESIGN.md`](../../../docs/DESIGN.md) (create if missing — OKLCH colors, Geist typography, elevation, components)       |
| Brand register     | Landing, signup, marketing, onboarding (design IS the product)                                                                |
| Product register   | `/orders`, `/logistics`, `/inventory`, `/warehouse` (design SERVES the product)                                               |

If `docs/PRODUCT.md` is missing, empty, or contains `[TODO]` placeholders, propose running `teach` (collect users / brand / tone / anti-refs / principles into the file) before doing any design work. Do NOT fabricate brand attributes.

If `docs/DESIGN.md` is missing, nudge the user once per session to run `document` (generate from existing tokens in `apps/web/src/lib/design-tokens.ts`, Tailwind config, and rendered pages), then proceed.

### 2. Register

Identify register before designing:

- **Brand register** — the surface IS the marketing. Landing, pricing, signup, public docs. Apply [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md) + impeccable brand rules.
- **Product register** — the surface SERVES the operator. `/orders`, `/logistics`, etc. Apply [`.cursor/skills/react-best-practices/SKILL.md`](../react-best-practices/SKILL.md) + impeccable product rules.

The first cue in the user's request wins. "Polish the landing" = brand. "Polish the logistics table" = product. If ambiguous, ask one question, do not guess.

## Shared design laws

These apply to every design, both registers. Match implementation complexity to vision: maximalism needs elaborate code, minimalism needs precision.

### Color

- OKLCH. Reduce chroma as lightness approaches 0 or 100.
- Never `#000` or `#fff`. Tint every neutral toward the brand hue (chroma 0.005–0.01).
- Pick a color strategy before picking colors: **Restrained** (tinted neutrals + ≤10% accent), **Committed** (one saturated 30–60%), **Full palette** (3–4 named roles), or **Drenched** (the surface IS the color). For Binexus default to Committed on the landing, Restrained in the operator panel.

### Theme

Dark vs light is never a default. Write the physical-scene sentence ("dispatcher on a 27" monitor in a warehouse at 7am") and let the scene force the answer. Not "tools look cool dark", not "light to be safe".

### Typography

- Body line length: 65–75ch cap.
- Hierarchy through scale + weight contrast (≥1.25 ratio between steps). Avoid flat scales.
- Sans default: Geist. Inter only when explicitly requested (Linear-style / GOV-style).
- Serif very discouraged as default. If used, articulate why this specific serif fits this specific brand. Banned defaults: Fraunces, Instrument Serif.

### Layout

- Vary spacing for rhythm. Uniform padding is monotony.
- Cards are the lazy answer. Use them only when truly the best affordance. Nested cards are always wrong.
- Don't wrap everything in a container.

### Motion

- Don't animate CSS layout properties (`width`, `height`, `top`, `left`).
- Ease out with exponential curves (`ease-out-quart`, `quint`, `expo`). No bounce. No elastic.

### Absolute bans (rewrite if you find these)

- Side-stripe borders (`border-l-4` colored accents on cards / callouts).
- Gradient text (`background-clip: text` on a gradient bg).
- Glassmorphism as default (`backdrop-filter` decoratively everywhere).
- Hero-metric template (big number + small label + supporting stats + gradient accent).
- Identical card grids (same-sized icon + heading + text cards repeated).
- Modal as first thought. Exhaust inline / progressive alternatives first.
- Em dashes anywhere. Use commas, colons, semicolons, periods, or parentheses.

### The AI slop test

If a reasonable person could look at the interface and say "AI made that" without doubt, it failed. Cross-register failures are the absolute bans above. Register-specific failures live in each reference.

**Category-reflex check** at two altitudes:

1. **First-order**: if someone could guess theme + palette from the category alone ("logistics → dark blue + orange truck"), it's the first training-data reflex. Rework.
2. **Second-order**: if someone could guess from category + anti-references ("logistics that's not navy → terminal-native dark mode"), the second reflex wasn't dodged either. Rework.

## Commands (Binexus mapping)

The upstream skill exposes these sub-commands. Invoke them by name. When in doubt, ask the user which they want.

| Command              | Use for…                                                                                                                                           |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `shape [feature]`    | Plan UX/UI before writing code. **Default for any new page** (landing section, signup, onboarding step).                                           |
| `craft [feature]`    | Shape, then build end-to-end. Use for "build the landing hero" / "ship the signup page".                                                           |
| `critique [target]`  | UX design review with heuristic scoring. Use before merging a public-facing PR.                                                                    |
| `audit [target]`     | Technical quality checks: a11y (Lighthouse ≥95), perf (LCP / CLS / INP), responsive at 320 / 768 / 1280 / 1920.                                    |
| `polish [target]`    | Final quality pass before shipping a landing page or onboarding flow.                                                                              |
| `bolder [target]`    | Safe / bland design needs amplification. Use when the design read says "premium consumer" and the current page reads "DocuSign".                   |
| `quieter [target]`   | Aggressive / overstimulating design needs to calm down. Use when the operator panel got animated by accident.                                      |
| `distill [target]`   | Strip to essence. Use when removing 30% of elements makes the page better.                                                                         |
| `harden [target]`    | Production-ready: errors, i18n (ES + EN), edge cases, empty states.                                                                                |
| `onboard [target]`   | Design first-run flows, empty states, activation. Pair with [`.cursor/skills/ui-ux-pro/SKILL.md`](../ui-ux-pro/SKILL.md).                          |
| `animate [target]`   | Purposeful motion. Pair with [`.cursor/skills/react-view-transitions/SKILL.md`](../react-view-transitions/SKILL.md).                               |
| `colorize [target]`  | Add strategic color to monochromatic UI.                                                                                                           |
| `typeset [target]`   | Improve typography hierarchy.                                                                                                                      |
| `layout [target]`    | Fix spacing, rhythm, visual hierarchy.                                                                                                             |
| `delight [target]`   | Personality and memorable touches. Use sparingly on landing only.                                                                                  |
| `overdrive [target]` | Push past conventional limits. Reserved for landing hero / pricing / "Coming Soon" pages.                                                          |
| `clarify [target]`   | UX copy, labels, error messages. Pair with [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md).                                           |
| `adapt [target]`     | Different devices / screen sizes.                                                                                                                  |
| `optimize [target]`  | Diagnose + fix UI performance (RSC boundaries, bundle size, hydration cost).                                                                       |
| `extract [target]`   | Pull reusable tokens / components into the design system. New extracts go into `packages/ui` (web) or `apps/web/src/lib/design-tokens.ts`.         |
| `live`               | Visual variant mode — pick elements in the browser, generate alternatives. Use when iterating on the landing hero with the user open in localhost. |

## How to invoke the full upstream skill

For the deep instruction set behind any command:

```bash
npx impeccable@latest <command> [target]
```

This loads the upstream skill text into the agent. Use sparingly — the full skill is large. For routine work, the [Shared design laws](#shared-design-laws) above and the command map are enough.

## Output discipline

Every design pass ends with:

1. A statement of the design read ("Reading this as ...").
2. The register applied.
3. The dial values used (variance / motion / density).
4. The absolute bans checked off as "not present".
5. The implementation as working code in `apps/web/`, not as a prose description.
6. A11y / perf evidence: Lighthouse score + LCP / CLS / INP screenshot, or an explicit note that this is a code-only iteration to be measured next.

## Reference

- Upstream skill (full instruction set + per-command references): [`skills/impeccable-main/skill/SKILL.md`](../../../skills/impeccable-main/skill/SKILL.md) and per-command pages under [`skills/impeccable-main/.cursor/skills/impeccable/reference/`](../../../skills/impeccable-main/.cursor/skills/impeccable/reference/)
- npm package: `npx impeccable@latest` (v2.1.9)
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md) — taste read + dial inference
- [`.cursor/skills/ui-ux-pro/SKILL.md`](../ui-ux-pro/SKILL.md) — UX patterns + design tokens
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md) — copy hygiene
- [`.cursor/skills/react-view-transitions/SKILL.md`](../react-view-transitions/SKILL.md) — motion
