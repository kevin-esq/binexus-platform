---
name: react-view-transitions
description: Add native browser View Transitions to the Binexus public landing and onboarding flow using React 19's `<ViewTransition>` component. Use for hero scroll reveals, section-to-section navigation on the landing, list-to-detail navigation on public pages, signup wizard step transitions, and shared-element animations (e.g. trial CTA → signup form). Do NOT use on the operator panel — dispatchers value zero-latency over animation polish.
---

# react-view-transitions (Binexus)

Native browser view transitions for the Binexus **public surface** and onboarding. Wrapped around React 19's `<ViewTransition>` + `document.startViewTransition`. Adapted from Vercel's `vercel-react-view-transitions`. Full ruleset + recipes: [`skills/agent-skills-main/skills/react-view-transitions/SKILL.md`](../../../skills/agent-skills-main/skills/react-view-transitions/SKILL.md).

## Where this applies

- `apps/web/src/app/(public)/**` — landing sections, pricing tiers, feature pages.
- `apps/web/src/app/(auth)/**` — login ↔ signup transitions.
- `apps/web/src/app/(onboarding)/**` — wizard step transitions (1 → 2 → 3).
- Any future public route group.

## Where this does NOT apply

- `/orders`, `/logistics`, `/inventory`, `/warehouse`, `/dispatch` — operator surfaces. Dispatchers click 200 times an hour; animation latency is friction. Use plain navigation.
- Modal opens / drawer opens inside the panel — use direct CSS `transition`, not view transitions.

## Availability check

- Next.js App Router (this repo's `apps/web`) already bundles a React canary internally. `<ViewTransition>` works out of the box. **Do not install `react@canary`** — it's already there and forcing the dependency breaks Next's resolver.
- `npm ls react` may show a stable-looking version. Expected.
- Browser support: Chromium 111+, Firefox 144+, Safari 18.2+. Older browsers skip animations silently.

## The five patterns (apply in order)

| Priority | Pattern                     | What it communicates             | Binexus example                                 |
| -------- | --------------------------- | -------------------------------- | ----------------------------------------------- |
| 1        | Shared element (`name`)     | "Same thing — going deeper"      | "Empieza prueba" CTA pill → signup card morph   |
| 2        | Suspense reveal             | "Data loaded"                    | Pricing tiers stream in as Stripe products load |
| 3        | List identity               | "Same items, new arrangement"    | Reordered feature blocks, testimonial carousel  |
| 4        | State change (enter/exit)   | "Something appeared/disappeared" | Onboarding step 1 → 2 panel slide               |
| 5        | Route change (layout-level) | "Going to a new place"           | `/` → `/pricing` (lateral, fade)                |

Implement every pattern that fits a given page. Skip only if the page has no use case for it. This is an order, not a "pick one" list.

## Directional vs lateral

| Context                                             | Animation                                          |
| --------------------------------------------------- | -------------------------------------------------- |
| Hierarchical: landing → signup → onboarding         | Type-keyed `nav-forward` / `nav-back` slide        |
| Lateral: pricing ↔ features ↔ about                 | Bare `<ViewTransition>` (fade) or `default="none"` |
| Suspense reveal (Stripe products, demo screenshots) | `enter` / `exit` string props                      |
| Revalidation / background refresh                   | `default="none"` — no animation                    |

The signup wizard (1 → 2 → 3) uses `nav-forward`; pressing the "Back" button uses `nav-back`. Reserve directional slides for ordered sequences — never for sibling tabs.

## Implementation

### 1. Import

```tsx
import { ViewTransition, startTransition } from 'react';
```

Never call `document.startViewTransition` yourself — let React orchestrate it.

### 2. Triggers

Only `startTransition`, `useDeferredValue`, or `Suspense` activate view transitions. Plain `setState` does not animate. If a transition does not fire, the caller is the bug, not the markup.

### 3. CSS recipes

Use the recipes in [`skills/agent-skills-main/skills/react-view-transitions/references/css-recipes.md`](../../../skills/agent-skills-main/skills/react-view-transitions/references/css-recipes.md). Never hand-write `::view-transition-old` / `::view-transition-new` rules — the recipes already cover fade, slide-forward, slide-back, scale-share. Copy into `apps/web/src/app/globals.css` once, reference by class.

### 4. Shared element example (Binexus signup CTA)

```tsx
// app/(public)/page.tsx
<ViewTransition name="signup-cta">
  <Link href="/signup">Empieza prueba gratis</Link>
</ViewTransition>

// app/(auth)/signup/page.tsx
<ViewTransition name="signup-cta">
  <h1>Crea tu tenant</h1>
</ViewTransition>
```

The pill morphs into the form heading. Same `name` on both sides triggers the share.

### 5. Wizard example (onboarding 1 → 2)

```tsx
const [step, setStep] = useState(1);
const next = () => {
  startTransition(() => setStep((s) => s + 1));
};

return (
  <ViewTransition key={step} enter="nav-forward" exit="nav-back">
    <Step step={step} />
  </ViewTransition>
);
```

`key={step}` is the unmount-mount trigger. `nav-forward` / `nav-back` are CSS classes from the recipes file.

## Anti-patterns

- Wrapping `<ViewTransition>` around `setState` calls that aren't inside `startTransition`. Nothing animates and the developer assumes the API is broken.
- Adding animation to `/orders` table row clicks. Dispatchers click 200 rows/hour.
- Mixing `motion` (the Framer successor) with View Transitions on the same element. Pick one.
- Reusing the same `name` for two different shared elements on the same page — collision logs and silent failure.
- Animating route changes on every page. Pages that ARE the destination (`/pricing` as a tab sibling of `/features`) should fade, not slide.
- Using `useState` in display-blocking animations. Use Motion's `useMotionValue` for continuous values; View Transitions for state-to-state morphs.

## Reduced motion

Always honor `prefers-reduced-motion`. The CSS recipes in the referenced file already gate the slide/morph animations behind `@media not (prefers-reduced-motion)`. Don't override that media query.

## Pre-PR checklist

- [ ] Each `<ViewTransition>` communicates a specific spatial relationship. If you can't articulate it in one sentence, remove the wrapper.
- [ ] No directional slide on lateral navigation.
- [ ] No `<ViewTransition>` inside operator routes.
- [ ] `prefers-reduced-motion` honored.
- [ ] CSS recipes imported once in `globals.css`, not duplicated per component.
- [ ] No `react@canary` install attempted.

## Reference

- Full skill + implementation order + recipes: [`skills/agent-skills-main/skills/react-view-transitions/SKILL.md`](../../../skills/agent-skills-main/skills/react-view-transitions/SKILL.md)
- CSS recipes: [`skills/agent-skills-main/skills/react-view-transitions/references/css-recipes.md`](../../../skills/agent-skills-main/skills/react-view-transitions/references/css-recipes.md)
- Implementation walkthrough: [`skills/agent-skills-main/skills/react-view-transitions/references/implementation.md`](../../../skills/agent-skills-main/skills/react-view-transitions/references/implementation.md)
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md) — dial `MOTION_INTENSITY` lives here
- [`.cursor/skills/ui-ux-pro/SKILL.md`](../ui-ux-pro/SKILL.md) — wizard structure
