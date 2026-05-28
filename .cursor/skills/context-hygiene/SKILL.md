---
name: context-hygiene
description: Reduce tokens-per-task on long Binexus sessions. Use proactively when a session is past ~60% context, when re-reading the same files repeatedly, when output starts losing file paths / event names / state names, or when the user asks for a handoff / summary / resume. Covers compaction triggers, observation masking, structured handoffs, and what NEVER to summarize away in this repo.
---

# context-hygiene (Binexus)

Token economy for long agent sessions on this codebase. Distilled from `Agent-Skills-for-Context-Engineering` (`context-compression`, `context-optimization`, `context-degradation`). The full references live in [`skills/Agent-Skills-for-Context-Engineering-main/skills/`](../../../skills/Agent-Skills-for-Context-Engineering-main/skills/).

The goal is **tokens-per-task**, not tokens-per-reply. Aggressive compression that drops file paths or event names forces re-exploration and costs more overall.

## When to act

Trigger this skill when ANY of these hold:

- The session is large (long transcript or known to exceed ~60% context budget).
- You catch yourself about to re-read the same file twice.
- Replies start using generic phrases ("the handler", "the config file") instead of exact names.
- Event names, state names, or model names get paraphrased.
- The user says "summary", "resume", "handoff", "continúa", "sigue donde quedamos".

## Anchors — never compress these

These identifiers are the spine of Binexus. Preserve verbatim in any summary / handoff / scratchpad:

| Category                      | Examples                                                                                                                                                                                               |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Prisma model / enum names     | `DeliveryRoute`, `DeliveryRouteStop`, `DeliveryProof`, `OrderState.READY_FOR_DELIVERY_ROUTE`, `DeliveryRouteStopStatus.DELIVERED`                                                                      |
| Event names                   | `ORDER_APPROVED`, `INVENTORY_RESERVED`, `PICKING_COMPLETED`, `ORDER_READY_FOR_DELIVERY_ROUTE`, `DELIVERY_ROUTE_DISPATCHED`, `DELIVERY_CONFIRMED`, `ORDER_DELIVERED`                                    |
| Command / handler class names | `CreateOrderCommand`, `DispatchDeliveryRouteCommand`, `ConfirmDeliveryHandler`, `DeliveryConfirmedOrdersHandler`                                                                                       |
| Bounded context paths         | `apps/backend/src/contexts/<context>/...`                                                                                                                                                              |
| Migration filenames           | `apps/backend/prisma/migrations/<timestamp>_<slug>/migration.sql`                                                                                                                                      |
| SDK methods                   | `client.confirmDelivery`, `client.listDeliveryRouteStops`, etc.                                                                                                                                        |
| Notion page IDs               | `36b91920-017f-811a-8cf0-ee25450352bc` (Home), `36b91920-017f-81e5-bf8a-c83e484c1bc6` (Roadmap), `36b91920-017f-810c-a682-ea078c0e4197` (Catálogo de eventos), and per-context IDs in notion-docs-sync |
| Tenant guarantees             | `TENANT_SCOPED_MODELS` set in `apps/backend/src/common/prisma/prisma.service.ts`, `tenantContext.run(...)` semantics                                                                                   |

If a summary would lose any of these, restructure it. They are cheaper to repeat than to re-derive.

## Cheap wins (apply first, in order)

1. **Don't re-read full files.** Use `Read` with `offset` / `limit` for files >300 lines. Re-read only the changed window.
2. **Don't `cat` / `Get-Content` files** — always use the `Read` tool so the IDE caches the slice.
3. **Search with `Grep` before reading.** Skip files whose name is enough.
4. **Prefer subagents (`Task subagent_type=explore`) for multi-file exploration.** They return synthesized findings, not raw file contents.
5. **Batch independent tool calls** in one message instead of sequential turns. Each turn re-tokenizes the prompt prefix.
6. **Strip noisy stdout.** When running `pnpm exec turbo run ...`, expect 100s of cached-log lines. After a green run, only quote the final `Tasks: N successful` line; do not paste the whole log into chat or follow-ups.

## Structured handoff template

When asked for a handoff, resume, or session summary, use exactly this shape. It maps to the slice cadence in [`.cursor/rules/common-development-workflow.md`](../../rules/common-development-workflow.md).

```markdown
## Session intent

<one line — what we are shipping>

## Current slice

<F<n> — <slice name>>, e.g. F4 Logistics — Delivery Proof Base

## Files modified

- <path>: <what changed> (preserve full path, never abbreviate)

## Files read but not changed

- <path>

## Decisions

- <decision> (idempotent on retry, route auto-complete, etc.)

## Events touched

- emitted: <EVENT_NAME>
- consumed: <EVENT_NAME>

## State transitions touched

- <Aggregate>: <FROM> -> <TO>

## Quality gates run

- pnpm exec turbo run typecheck lint build --filter=<...>: <green | failing X>
- pnpm --filter @binexus/backend test -- <spec>: <N passed / Y failed>

## Docs / Notion touched

- docs/<...>
- Notion: <page name>

## Next step

<single concrete next action>
```

Pair this with [`.cursor/skills/handoff/SKILL.md`](../handoff/SKILL.md) for the human-facing format.

## Compaction triggers

If you maintain a scratchpad (e.g. plan-mode plan, notes file, transcript window):

- At ~70% utilization: compact older turns into the handoff template above. Keep the most recent 3 turns verbatim.
- Never compress the user's original request — the constraints in it usually cannot be re-derived.
- Never compress tool schemas, the Prisma schema, or `packages/types/src/orders.ts` (`canTransition()`) — they are the source of truth.

## Re-exploration alarm

If during a session you find yourself about to:

- Run `Glob` for a file you already opened, OR
- `Grep` for an event name already discussed, OR
- Read a `docs/domains/<x>.md` you already read,

stop. The previous output already had the answer. Re-state the relevant anchor (filename, line range, decision) from earlier in the session before fetching again. If you cannot, that is the signal that the session needs a structured handoff (above) and a fresh session resume.

## Anti-patterns

- Summaries that say "the logistics module" instead of `apps/backend/src/contexts/logistics`.
- Summaries that say "the dispatch event" instead of `DELIVERY_ROUTE_DISPATCHED`.
- Pasting entire `turbo run` cached logs into a follow-up message.
- Re-running `prisma generate` "to be safe" when no schema changed in the session.
- Re-reading `apps/backend/prisma/schema.prisma` end-to-end when only one model is relevant — use line offsets.

## References

- [`skills/Agent-Skills-for-Context-Engineering-main/skills/context-compression/SKILL.md`](../../../skills/Agent-Skills-for-Context-Engineering-main/skills/context-compression/SKILL.md)
- [`skills/Agent-Skills-for-Context-Engineering-main/skills/context-optimization/SKILL.md`](../../../skills/Agent-Skills-for-Context-Engineering-main/skills/context-optimization/SKILL.md)
- [`.cursor/skills/handoff/SKILL.md`](../handoff/SKILL.md)
- [`.cursor/skills/zoom-out/SKILL.md`](../zoom-out/SKILL.md)
