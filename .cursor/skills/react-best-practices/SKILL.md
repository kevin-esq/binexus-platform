---
name: react-best-practices
description: Performance and code-quality guardrails for the Binexus web app (`apps/web` — Next.js 15 App Router + React 19 + Tailwind). Use when writing or reviewing React Server Components, Client Components, data fetching, route handlers, or anything under `apps/web/src/app/**`. Triggered by React/Next.js performance, bundle size, hydration, RSC vs Client, or fetch waterfalls.
---

# react-best-practices (Binexus)

Performance + correctness rules for [`apps/web`](../../../apps/web). Adapted from Vercel's `vercel-react-best-practices`. Full rule list: [`skills/agent-skills-main/skills/react-best-practices/SKILL.md`](../../../skills/agent-skills-main/skills/react-best-practices/SKILL.md) and the per-rule files in [`skills/agent-skills-main/skills/react-best-practices/`](../../../skills/agent-skills-main/skills/react-best-practices/).

## What we ship in `apps/web`

- Next.js 15 App Router (`apps/web/src/app/**`).
- React 19, Server Components by default, Client Components opt-in via `'use client'`.
- Data flows through `@binexus/sdk` against the NestJS backend at `:3001`.
- Tailwind for styling. No CSS-in-JS.
- The web app is dispatcher/admin-grade, not consumer marketing. Optimize for latency and operator UX, not for SEO.

## Priority rules

Apply in order. Stop at the first one that solves the issue.

### 1. Don't waterfall fetches (CRITICAL)

- Start independent requests in parallel with `Promise.all`. See `/logistics` for the pattern: candidates, planned, dispatched, and completed routes load in one `Promise.all` call.
- Push auth checks before awaits when they're cheap.
- Use Suspense boundaries to stream content where blocking the full page is wrong (e.g. order detail with lazy line items).

### 2. Keep the client bundle thin (CRITICAL)

- Default to Server Components. Only mark a tree `'use client'` if it uses `useState`, `useEffect`, event handlers, or browser APIs.
- Import from leaf modules, not barrel files (`from '@binexus/types/orders'`, not `from '@binexus/types'` when only one type is needed — if SDK barrel is the actual consumer, that's fine because the barrel re-exports are tree-shaken).
- Lazy-load heavy widgets with `next/dynamic`. Anything that pulls in chart / map / editor libraries belongs behind `dynamic(() => import('...'), { ssr: false })`.
- Hoist static I/O (logos, fonts) to module scope so it gets cached across requests.

### 3. Server side: respect the boundary

- Do NOT call `@binexus/sdk` from a Server Component with the same JWT as the user — use the server-side token storage flow (currently `apps/web/src/lib/token-storage.ts`). If a Server Component needs data, fetch it through a server-only helper, not the client SDK.
- No module-level mutable state in RSC / Route Handlers. Multi-tenant data MUST flow through the request, never globals.
- Minimize props serialized from Server to Client. Don't pass the entire `Order` row to a client component that only needs `id` and `state`.

### 4. Client side: data fetching

- For interactive pages (`/logistics`, `/warehouse`, `/inventory`, `/orders`), the established pattern is: `useEffect` + `api.<method>()` + local state. Keep that until we add SWR or React Query.
- If you add SWR or React Query, do it as a single PR, not piecemeal.
- Use passive event listeners on scroll / wheel handlers.
- Version any `localStorage` schema with a key prefix (`binexus:v1:<key>`).

### 5. Re-renders

- Don't subscribe to state you only read in a callback. Use refs or selectors.
- Memoize expensive subtrees with `React.memo` only after measuring.
- Hoist non-primitive default props to module scope so identity is stable.

### 6. Forms and inputs

- Native HTML inputs + Tailwind classes. No form libraries until justified.
- Server actions are allowed for write paths if they call into the NestJS backend. Authenticate them the same way as API routes.

### 7. Errors and loading

- Each route segment owns its own `loading.tsx` / `error.tsx` if the data fetch is slow or fallible. `/orders/[id]` is the reference.
- Surface backend errors verbatim in dev; surface friendlier copy in prod (covered by [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md)).

## Binexus-specific gotchas

- **Don't query Prisma from the web app.** All DB access goes through `@binexus/sdk` and the NestJS backend. The web app does NOT import `@prisma/client`.
- **Don't bypass `TenantContextService`.** Every API call flows through the SDK which carries the JWT. Adding a "fetch helper" that hits the backend without auth headers breaks multi-tenancy.
- **Don't pretend events.** The web app is read-only against the event bus. It reacts to refreshes / SWR / route revalidation, never to in-process events.
- **Don't add OG / SEO metadata** to internal pages — this is an operator UI, indexing is not a requirement.

## Pre-PR checklist

- [ ] Any new Client Component justified by state / events / browser APIs?
- [ ] Independent fetches are `Promise.all`'d?
- [ ] No barrel-imported icon libraries that pull in megabytes?
- [ ] No module-level mutable state in `apps/web/src/app/**`?
- [ ] No direct Prisma / DB imports?
- [ ] `pnpm exec turbo run typecheck lint build --filter=@binexus/web` is green?

## Reference

- Rule catalog: [`skills/agent-skills-main/skills/react-best-practices/`](../../../skills/agent-skills-main/skills/react-best-practices/)
- [`.cursor/skills/composition-patterns/SKILL.md`](../composition-patterns/SKILL.md)
- [`.cursor/skills/nextjs-turbopack/SKILL.md`](../nextjs-turbopack/SKILL.md)
- [`apps/web/src/app/logistics/page.tsx`](../../../apps/web/src/app/logistics/page.tsx) — reference for parallel fetch + interactive table.
