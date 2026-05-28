---
name: composition-patterns
description: React component composition guidance for `apps/web` — avoid boolean-prop proliferation, use compound components, lift state via providers. Use when adding props to an existing component, building a reusable widget, designing a table / form / detail-view, or refactoring `apps/web/src/app/**` components. Pairs with `react-best-practices`.
---

# composition-patterns (Binexus)

Composition rules so the Binexus admin UI scales without prop explosions. Adapted from Vercel's `vercel-composition-patterns`. Full rules: [`skills/agent-skills-main/skills/composition-patterns/SKILL.md`](../../../skills/agent-skills-main/skills/composition-patterns/SKILL.md).

## When to apply

- A component you are editing has 4+ boolean props (`showHeader`, `editable`, `compact`, `withActions`, ...).
- You are about to add yet another boolean prop to "customize behaviour".
- You are designing a widget that will be reused across `/orders`, `/logistics`, `/warehouse`, `/inventory`.
- You are tempted to copy-paste a JSX block 3+ times.

## Rules in order of impact

### 1. Avoid boolean prop proliferation (HIGH)

Each boolean prop multiplies states (`2^N` configurations). Instead:

- Accept `children` and let the caller assemble the inside.
- Split into explicit variants (`<RouteRowPlanned>`, `<RouteRowDispatched>`, `<RouteRowCompleted>`) rather than `<RouteRow status={...} editable canConfirm canAssign />`.
- Group related controls behind a `slots` object only when variants would explode.

Bad:

```tsx
<DeliveryRouteRow editable showDriver showStops showCompletedAt withDispatchAction />
```

Good (compound):

```tsx
<DeliveryRouteRow route={route}>
  <DeliveryRouteRow.Driver />
  <DeliveryRouteRow.Stops />
  <DeliveryRouteRow.Actions>
    <DispatchButton route={route} />
  </DeliveryRouteRow.Actions>
</DeliveryRouteRow>
```

### 2. Compound components share state via context

For the compound pattern above, lift state into a provider that knows the row's data. Children read it from `useContext`. The provider is the only place that knows how state is managed; consumers stay declarative.

When you create a context, follow the interface shape used elsewhere in `apps/web`:

```tsx
interface DeliveryRouteRowContext {
  state: { route: DeliveryRouteSummary; expanded: boolean };
  actions: { toggleStops: () => void; refresh: () => Promise<void> };
  meta: { loadingStops: boolean };
}
```

This `state / actions / meta` split keeps the consumer's `useContext` calls explicit and lets you mock the provider in tests by injection.

### 3. Lift state to enable sibling coordination

If two siblings need to coordinate (e.g. "selecting a stop" highlights the row + enables an action button), move state up into the parent or a provider. Don't thread props through 3 levels.

In `apps/web/src/app/logistics/page.tsx` the page-level state (`expandedRouteId`, `confirmingStopId`) is the canonical example. Keep that pattern when the surface stays small. Promote to a provider once the page hits ~5+ piece-of-state.

### 4. Render-prop sparingly, slots more often

React 19 makes children + context cheap. Render props are a fallback when:

- Children need data only the parent owns and context would be overkill.
- A library API needs to expose internal state without a provider.

Otherwise prefer slots (named children) or context.

### 5. Hooks own behavior, components own shape

Extract repeated `useEffect` + `useState` logic into hooks named `useDeliveryRoutes`, `useConfirmDelivery`, etc. The component file should read like JSX + handler wires. If a component file has more `useState` than `<jsx>`, extract.

### 6. React 19 specifics

- `use(promise)` is available in Server Components — prefer it over a Suspense + library wrapper when the data is request-scoped.
- `useActionState` for forms tied to server actions.
- `useFormStatus` for inline submission state inside a form's button.

## Binexus-specific gotchas

- **No global Redux / Zustand store.** The current app uses page-level `useState` + SDK calls. Don't add a global store without an ADR.
- **Don't pull in design-system libs** (MUI, Mantine, Chakra). Tailwind + plain JSX is the convention. The shared bits live in [`packages/ui`](../../../packages/ui).
- **Server vs Client.** A compound component with hooks must be `'use client'`. The wrapper that fetches data can stay a Server Component, passing data into the client compound.

## Pre-PR checklist

- [ ] Any new boolean prop justified, or could it be a variant / slot?
- [ ] Compound components share state via context, not prop drilling >2 levels.
- [ ] Hooks named after the behavior, not the component.
- [ ] No new global store introduced without ADR.
- [ ] `pnpm exec turbo run typecheck lint build --filter=@binexus/web` green.

## Reference

- Full rule list: [`skills/agent-skills-main/skills/composition-patterns/SKILL.md`](../../../skills/agent-skills-main/skills/composition-patterns/SKILL.md)
- [`.cursor/skills/react-best-practices/SKILL.md`](../react-best-practices/SKILL.md)
- [`apps/web/src/app/logistics/page.tsx`](../../../apps/web/src/app/logistics/page.tsx) — current state-lifting pattern
- [`packages/ui`](../../../packages/ui) — shared primitives
