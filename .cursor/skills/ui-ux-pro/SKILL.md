---
name: ui-ux-pro
description: Premium UX patterns and design-intelligence reference for the Binexus public landing, free-trial signup, onboarding wizard, and first-run experience. Use when designing flows that convert prospects into tenants, when picking color palette / typography / spacing tokens for the landing, or when reviewing UX of any page that a non-operator user sees. Wraps the upstream `ui-ux-pro-max` skill (67 UI styles, 161 palettes, 57 font pairings, 99 UX guidelines). Pairs with `taste` for visual direction.
---

# ui-ux-pro (Binexus)

Premium UX guidance for the public-facing surface of Binexus: landing, signup, onboarding, first-run, billing portal. Wraps the upstream `ui-ux-pro-max` skill and adapts it to the Binexus stack and audience.

Upstream skill: `ui-ux-pro-max` (v2.5.0). Install reference (if/when you want the full local skill set):

```bash
npx uipro-cli init --ai cursor
```

That writes a `.cursor/skills/ui-ux-pro-max/` tree with the full 67 styles / 161 palettes / 57 font pairings / 99 UX guidelines. Optional — do NOT run during a coding session unless the user asks. This local skill is the curated subset that fits Binexus.

## Where this applies

- `apps/web/src/app/(public)/**` — landing, pricing, public docs.
- `apps/web/src/app/(auth)/**` — signup, login, password reset.
- `apps/web/src/app/(onboarding)/**` — first-run wizard for a new tenant.
- `apps/web/src/app/billing/**` (when F7 ships) — billing portal for tenant admins.

Not for `/orders`, `/logistics`, `/inventory`, `/warehouse`, `/dispatch` (operator panel).

## 1. Audience model

The Binexus public surface speaks to three personas:

| Persona           | Reads landing for                        | Convert via                          |
| ----------------- | ---------------------------------------- | ------------------------------------ |
| Ops manager       | "Will this replace my Excel + WhatsApp?" | Free trial, 1 tenant, ≤5 routes/day  |
| Small fleet owner | "Cuánto cuesta, cuánto demora arrancar?" | Free trial, then assisted onboarding |
| Enterprise eval   | "Multi-tenant? Soporte? Integraciones?"  | Demo booking → custom POC            |

Every public page must serve at least one of these. If it serves none, it should not exist.

## 2. Conversion patterns

### 2.1 Landing → free trial

- Single CTA above the fold: "Empieza prueba gratis" (or EN equivalent).
- The CTA leads to a single-screen signup form: `email`, `password`, `tenantName`. No "country" / "industry" / "team size" pre-trial. Ask in onboarding.
- After signup, `POST /tenants` creates the tenant + first user, returns a JWT, redirects to the onboarding wizard.

### 2.2 Onboarding wizard

Three steps maximum for first-run. The wizard is the moment the user decides whether to come back tomorrow.

| Step | Goal                                              | Skip-able?                            |
| ---- | ------------------------------------------------- | ------------------------------------- |
| 1    | Create first branch                               | No                                    |
| 2    | Import or create first order(s)                   | Yes (with a "use sample data" button) |
| 3    | Land on `/orders` with at least one order visible | Final step                            |

Every wizard step:

- Single primary action visible at all times.
- Progress shown as `1 / 3`, never a vague "Almost there".
- "Skip for now" is allowed only on step 2.
- The wizard ends by routing to `/orders` with the new orders highlighted, NOT to an empty `/dashboard`.

### 2.3 Empty states (operator panel)

When the operator panel does have an empty state (no orders yet, no routes yet), this skill still applies. Empty states are the second-most-important moment after the first wizard. Each empty state has:

1. A one-line description of what lives here.
2. A primary action (create, import, link).
3. A link to the relevant `docs/` page in `apps/web`.

Never ship the default "No data" + grey icon.

## 3. Design tokens

Tokens that any new public page MUST reuse instead of inventing. Define once in `apps/web/src/lib/design-tokens.ts` (create if missing) and import from there.

```ts
export const tokens = {
  brand: {
    primary: 'oklch(0.55 0.15 250)', // committed accent — set during taste pass
    primaryHover: 'oklch(0.5 0.15 250)',
  },
  neutral: {
    bg: 'oklch(0.99 0.005 250)',
    surface: 'oklch(0.97 0.005 250)',
    border: 'oklch(0.9 0.005 250)',
    textMuted: 'oklch(0.55 0.01 250)',
    textPrimary: 'oklch(0.2 0.01 250)',
  },
  radius: { sm: '0.375rem', md: '0.5rem', lg: '0.75rem', pill: '9999px' },
  shadow: { sm: '0 1px 2px rgba(0,0,0,0.05)', md: '0 4px 12px rgba(0,0,0,0.08)' },
} as const;
```

Adjust hues during the `taste` pass; freeze the structure.

## 4. Typography pairings (Binexus rotation)

Pick one pairing per project surface, lock it for the life of that surface. Never mix.

| Surface        | Sans        | Mono           | Display              |
| -------------- | ----------- | -------------- | -------------------- |
| Landing        | Geist       | Geist Mono     | Geist (heavy weight) |
| Onboarding     | Geist       | Geist Mono     | — (no display layer) |
| Docs           | Inter Tight | JetBrains Mono | —                    |
| Billing portal | Geist       | Geist Mono     | —                    |

Forbidden as defaults: Inter (regular), Fraunces, Instrument Serif.

## 5. UX guidelines (top 12, in order of impact)

Curated subset from the upstream 99. Apply in order.

1. **Recovery beats prevention.** Forms always have a "back" path that preserves state. Destructive ops always have an "undo" within the next 5 seconds.
2. **One primary action per screen.** Everything else is secondary or tertiary.
3. **Names match the user's vocabulary, not yours.** "Ruta de entrega" beats "Delivery route" for ES audiences; "Centro" beats "Branch" for some tenants. Localize the surface, not the data model.
4. **Loading states must say what's loading.** "Cargando órdenes..." beats a spinner.
5. **Errors are conversational.** "No pudimos crear el tenant — el correo ya existe." Not "ERR_DUPLICATE_EMAIL".
6. **Confirmation only when destructive.** Saving a form does not need confirmation. Deleting a tenant does.
7. **Show, don't navigate, when possible.** Open the order detail inline (drawer / accordion) rather than full route change, when the user is in the middle of a list-task.
8. **The skeleton matches the real layout.** A skeleton that does not match the rendered table is more disorienting than a spinner.
9. **Disabled states explain themselves.** A disabled "Dispatch route" button has a tooltip: "Necesitas al menos una parada planificada".
10. **Time is local, money is explicit, units are visible.** "Hoy 14:30 UTC-5", "$12.500 MXN", "120 kg" — never strip the suffix.
11. **Empty states pre-populate.** New tenant has a "Crear pedido de ejemplo" button that triggers the seed.
12. **Tooltips never hold critical info.** Anything required to act lives in the body.

## 6. Component shape

Compose with the patterns in [`.cursor/skills/composition-patterns/SKILL.md`](../composition-patterns/SKILL.md). For the public surface specifically:

- `<Section>` — vertical band of the landing, takes `<Section.Header>`, `<Section.Body>`, `<Section.CTA>`.
- `<Hero>` — variant of `<Section>` with eyebrow + headline + sub + CTA + media slot.
- `<FeatureRow>` — alternating left/right copy + screenshot, NOT a card grid.
- `<PricingTable>` — three tiers max. Mark one tier "Recomendado" only if the data shows it converts best.
- `<OnboardingStep>` — wizard step shell with progress, skip (if allowed), primary action.

All of these live in [`packages/ui`](../../../packages/ui) once stable.

## 7. Accessibility (non-negotiable)

- Lighthouse a11y ≥ 95 on every public route. Block merge below.
- Color contrast: WCAG AA on every copy / background combo. Run automated check + manual check on tinted neutrals.
- Keyboard: tab order matches visual order. Every CTA reachable.
- Reduced motion: `prefers-reduced-transparency` and `prefers-reduced-motion` respected. No mandatory animations for state changes.
- Screen reader: aria-labels on every icon-only button. Live regions on form errors.

## 8. Pre-PR checklist

- [ ] Audience persona named in PR body.
- [ ] Tokens come from `apps/web/src/lib/design-tokens.ts`, no hard-coded hexes.
- [ ] One primary action per screen.
- [ ] Empty states implemented for the new surface.
- [ ] Lighthouse a11y ≥ 95.
- [ ] No copy contains LLM tells (run `stop-slop`).
- [ ] Cards / grids / heroes don't match the bans in `taste`.

## Reference

- Upstream skill (full 67/161/57/99 reference): https://uupm.cc (no local copy — install via `npx uipro-cli init --ai cursor` only when you want everything).
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md)
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md)
- [`.cursor/skills/composition-patterns/SKILL.md`](../composition-patterns/SKILL.md)
- [`.cursor/skills/react-view-transitions/SKILL.md`](../react-view-transitions/SKILL.md)
- [`apps/web/src/lib`](../../../apps/web/src/lib) — where design tokens live.
