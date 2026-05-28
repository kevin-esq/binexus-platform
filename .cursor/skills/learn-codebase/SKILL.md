---
name: learn-codebase
description: Prime context on the Binexus modular monolith in the right order — docs first, then bounded contexts, then code. Use at the start of a new session, when onboarding to a new bounded context, when the user says "learn the codebase / read the codebase / prime / get up to speed / aprende el código / ponte al día", or before tackling a slice in a context you have not touched this session. Token-efficient: reads architecture docs as anchors instead of streaming every source file.
---

# learn-codebase (Binexus)

Build a working mental model of the Binexus platform before doing slice work. The default flow ("read every file") is wrong for this repo — it has growing `packages/`, two apps, generated Prisma client, lockfiles, and tests. Use the docs as anchors and pull code on demand.

Adapted from `claude-mem-main/plugin/skills/learn-codebase` (whose default is "read every source file in full"). For a brand-new repo without docs that default would be right; Binexus has rich docs, so we pivot the strategy. Original: [`skills/claude-mem-main/plugin/skills/learn-codebase/SKILL.md`](../../../skills/claude-mem-main/plugin/skills/learn-codebase/SKILL.md).

## Pivot rule

> **Docs are anchors, code is detail.** Read the docs end-to-end. Read code only for the bounded contexts you will touch this session, and inside those contexts read commands and event handlers before reading services.

## Reading order

### 1. Project shape (always)

- [`README.md`](../../../README.md) — stack and ports.
- [`docs/architecture/overview.md`](../../../docs/architecture/overview.md)
- [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md)
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md)
- [`docs/architecture/multi-tenant.md`](../../../docs/architecture/multi-tenant.md)
- [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md)
- [`docs/events/README.md`](../../../docs/events/README.md) — event catalog.
- [`docs/states/order.md`](../../../docs/states/order.md) — the central state machine.

### 2. Active contexts (always at least scan)

For each context with a `docs/domains/<name>.md`, read it. Status as of latest sync:

- `identity`, `orders`, `inventory`, `warehouse`, `logistics` — active.
- `catalog`, `customers`, `sales`, `billing`, `reporting` — placeholders.

Active contexts each follow the layout:

```
apps/backend/src/contexts/<context>/
  application/commands/<verb>-<noun>.command.ts
  application/<noun>-read.service.ts
  events/<event-name>.handler.ts
  presentation/<context>.controller.ts
  <context>.module.ts
```

Inside each active context, prime in this order: `<context>.module.ts` → `presentation/*.controller.ts` → `application/commands/*.command.ts` → `events/*.handler.ts` → services. Stop as soon as you have what the current slice needs.

### 3. Cross-cutting foundations (read once per session, then trust)

- [`apps/backend/src/common/prisma/prisma.service.ts`](../../../apps/backend/src/common/prisma/prisma.service.ts) — `TENANT_SCOPED_MODELS`, `forTenant()`.
- [`apps/backend/src/common/tenant/tenant-context.service.ts`](../../../apps/backend/src/common/tenant/tenant-context.service.ts) — `ALS` semantics, `run()` for system users.
- [`apps/backend/src/common/events/event-bus.service.ts`](../../../apps/backend/src/common/events/event-bus.service.ts) and [`outbox.service.ts`](../../../apps/backend/src/common/events/outbox.service.ts) — emit pattern.
- [`apps/backend/src/common/commands/app-command.ts`](../../../apps/backend/src/common/commands/app-command.ts) — base command + metadata.
- [`packages/types/src/orders.ts`](../../../packages/types/src/orders.ts) — `canTransition()` is the source of truth for the order state machine.

### 4. Shared packages (skim on demand)

- [`packages/events`](../../../packages/events) — registry + Zod schemas. Read the index files, not every schema.
- [`packages/types`](../../../packages/types) — branded ID types, summary shapes per context.
- [`packages/sdk`](../../../packages/sdk) — HTTP client used by `apps/web`.
- [`packages/ui`](../../../packages/ui) — shared web primitives (small).
- [`packages/config`](../../../packages/config) — env + logging config (mostly stable).

### 5. Schema and migrations

- [`apps/backend/prisma/schema.prisma`](../../../apps/backend/prisma/schema.prisma) — read the enum + model blocks for the contexts you will touch. Use `Read` with `offset`/`limit`. Don't stream the whole file unless you really need to.
- Migrations under [`apps/backend/prisma/migrations/`](../../../apps/backend/prisma/migrations/) are append-only history — only read the latest one for the context you touch.

### 6. Web app (only if the slice touches UI)

- [`apps/web/src/app/orders/page.tsx`](../../../apps/web/src/app/orders/page.tsx) — reference for table + cursor pagination.
- [`apps/web/src/app/logistics/page.tsx`](../../../apps/web/src/app/logistics/page.tsx) — reference for parallel fetch + expandable rows + actions.
- [`apps/web/src/lib/api.ts`](../../../apps/web/src/lib/api.ts) and [`token-storage.ts`](../../../apps/web/src/lib/token-storage.ts) — auth flow.

## Notion in parallel

The `notion-docs-sync` skill defines the matching Notion pages. Before working in a context, also fetch the Notion page for that context (page IDs are listed in [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md)). Cross-check that `Ahora` / Roadmap reflect the same state as `docs/`. If they drift, fix it as part of the slice — that's the contract of `notion-docs-sync`.

## What NOT to read during priming

These will pollute context without value:

- `pnpm-lock.yaml`, `node_modules/**`, `dist/**`, `.turbo/**`, `.next/**`.
- Generated Prisma client (`node_modules/.prisma/**`).
- Test fixture data and snapshot JSON.
- Every individual migration file — only the most recent in the context you touch.
- The full `skills/` tree (these are vendored references; the originals are huge).

## When you DO need to read every file

Reserve the original "read every source file" pattern for tiny, unfamiliar sub-projects (`packages/config`, a brand-new app). For `apps/backend` and `apps/web`, never. Use [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md) anchors instead.

## Reference

- [`.cursor/skills/zoom-out/SKILL.md`](../zoom-out/SKILL.md) — when you also need the why before the how.
- [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md) — keep what you learn cheap to reuse later in the session.
- [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md) — pair docs with Notion every time.
- Original skill: [`skills/claude-mem-main/plugin/skills/learn-codebase/SKILL.md`](../../../skills/claude-mem-main/plugin/skills/learn-codebase/SKILL.md).
