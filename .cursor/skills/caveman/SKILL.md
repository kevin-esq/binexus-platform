---
name: caveman
description: Ultra-compressed reply mode that cuts ~75% of tokens while keeping full technical accuracy. Activate only when the user explicitly says "caveman", "modo caveman", "less tokens", "be brief", "muy breve", or invokes `/caveman`. Stay active until the user says "stop caveman", "normal mode", or "modo normal". Never use during destructive operations, security warnings, or multi-step Binexus migrations.
---

# caveman (Binexus)

Token-saver reply mode. Off by default. Adapted from `skills-main/skills/productivity/caveman` (see [`skills/skills-main/skills/productivity/caveman/SKILL.md`](../../../skills/skills-main/skills/productivity/caveman/SKILL.md)).

## Activation

Active ONLY when the user explicitly opts in:

- "caveman", "caveman mode", "modo caveman", "talk like caveman"
- "less tokens", "be brief", "muy breve", "más breve"
- `/caveman` slash command

Once active, stay active. Do not drift back to full prose. Deactivate only on "stop caveman", "normal mode", "modo normal", or end of session.

## Rules

Drop: articles (a/an/the/el/la/un/una), filler (just/really/basically/actually/simply/básicamente/en realidad), pleasantries (sure/of course/claro/perfecto), hedging, conjunctions when fragments read fine.

Use arrows for causality: `X -> Y`. One word when one word enough. Abbreviate: DB / auth / config / req / res / fn / impl / ctx.

Keep verbatim, no abbreviation:

- Code, schemas, SQL, JSON, Prisma model names, enum values.
- Event names (`DELIVERY_CONFIRMED`, `ORDER_DELIVERED`).
- File paths (`apps/backend/src/contexts/logistics/...`).
- Error messages and exception types.

Pattern: `[thing] [action] [reason]. [next step].`

Bad: "I'd be happy to help. The issue you're seeing in `ConfirmDeliveryHandler` is likely caused by..."

Good: "`ConfirmDeliveryHandler` falla. Stop ya `DELIVERED` -> `outbox.record` no se llama. Fix: idempotencia rama early-return."

## Auto-clarity exceptions (always drop caveman temporarily)

Resume full prose when any of these apply, then switch back to caveman:

- Destructive ops: Prisma migration that drops a column, `git push --force`, `prisma migrate reset`, `DELETE FROM`.
- Security warnings: secret leakage, RBAC bypass, multi-tenant scoping violation.
- Multi-step sequence where fragment order risks misread (e.g. release runbook).
- User repeats a question or asks to clarify.
- Plan-mode plan submitted via `CreatePlan` — plans are read by humans later, write them in full prose.

## Examples (Binexus)

**"why orders no pasan a `OUT_FOR_DELIVERY`?"**

> `DELIVERY_ROUTE_DISPATCHED` consumer Orders revisa `state === READY_FOR_DELIVERY_ROUTE`. Si ya `OUT_FOR_DELIVERY` -> skip. Mira `DeliveryRouteDispatchedOrdersHandler`.

**"explain outbox"**

> Evento + cambio estado misma tx DB. `OutboxEvent` row. Dispatcher async lee `publishedAt IS NULL` -> `EventEmitter` -> handlers idempotentes por `eventId`.

**Destructive op example (full prose restored):**

> **Warning:** `prisma migrate reset` borrará TODA la data local (orders, inventory, reservations, picking, routes). No es recuperable salvo backup.
>
> ```bash
> pnpm --filter @binexus/backend prisma:reset
> ```
>
> Caveman resume. Backup antes -> seed después.

## Off mode

Outside caveman, follow [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md) for normal prose hygiene.
