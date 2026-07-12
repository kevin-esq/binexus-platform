---
name: semantic-naming
description: Choose explicit, business-meaningful, framework-safe names for Prisma/EF models, enums, commands, events, shared types, SDK methods, HTTP routes, DTOs, and C# Domain/Application contracts. Use when introducing or renaming any cross-boundary concept in packages/types, packages/events, packages/sdk, schema.prisma, apps/backend Modules Domain/Application/Features, migrations, or the web UI. Use proactively before generating migrations, command classes, event names, or shared type files.
---

# Semantic Naming

Binexus is a modular monolith. Names cross bounded contexts through `@binexus/types`, `@binexus/events`, `@binexus/sdk`, the web UI, and the database. A bad name is hard to undo once it ships through migrations, events, and the SDK. Stop and check the name **before** writing the code.

The source of truth is [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md). This skill is the always-on reminder that pairs with it.

## When to invoke

Always invoke this skill before:

- Adding or renaming a Prisma model, enum, or field that other contexts will see.
- Adding a domain event name to `packages/events/src/registry.ts`.
- Adding a command (`*.command.ts`), command handler, or controller route.
- Adding or renaming a shared type in `packages/types/`.
- Adding an SDK method in `packages/sdk/src/client.ts`.
- Designing a new context's vocabulary in a `docs/domains/<context>.md`.

If the user is "just" writing a UI label or DTO that maps to one of the above, still invoke this skill - the label is the externally visible name.

## Five-question checklist

Run this checklist for every new name before generating code:

```
- [ ] 1. Is it an explicit business noun? (no `Item`, `Task`, `Job`, `Tx`, `Move` on its own)
- [ ] 2. Does it collide with a framework concept? (`Route`, `Dispatch`, `Handler`, `Service`, `Module`, `Controller`)
- [ ] 3. Is it consistent with neighbour contexts? (`StockTransfer` -> `DeliveryRoute`, not `Route`)
- [ ] 4. Does it still make sense in `packages/types` or the SDK with no surrounding context?
- [ ] 5. Is the same term used across model, command, event, DTO, route, SDK method, UI label, docs?
```

If any answer is "no" or "not sure", propose a qualified alternative and surface the trade-off in chat before writing code.

## Conventions cheat sheet

| Artifact           | Convention                                  | Example                                          |
| ------------------ | ------------------------------------------- | ------------------------------------------------ |
| Prisma model       | `PascalCase`, explicit business noun        | `StockTransfer`, `PickingTask`, `DeliveryRoute`  |
| Enum               | `PascalCase` name, `SCREAMING_SNAKE` values | `StockTransferStatus { PENDING, RECEIVED, ... }` |
| Command class      | `<Verb><Noun>Command`                       | `CreateStockTransferCommand`                     |
| Handler            | `<Verb><Noun>Handler`                       | `CreateStockTransferHandler`                     |
| Event name         | `SCREAMING_SNAKE`, past tense, qualified    | `STOCK_TRANSFER_CREATED`, `PICKING_COMPLETED`    |
| Event payload type | `<EventName>Payload`                        | `StockTransferCreatedPayload`                    |
| HTTP route         | Plural, kebab-case, qualified noun          | `POST /inventory/stock/transfers`                |
| Shared type        | `PascalCase`, qualified noun                | `StockTransferSummary`                           |
| SDK method         | `<verb><Noun>()`                            | `createStockTransfer()`                          |

## Banned / risky names

Avoid these bare names. Always qualify with the domain:

| Avoid          | Use instead                |
| -------------- | -------------------------- |
| `Route`        | `DeliveryRoute`            |
| `Dispatch`     | `DispatchHandoff`          |
| `Transfer`     | `StockTransfer`            |
| `Task`         | `PickingTask`              |
| `Item`         | `StockItem`, `OrderLine`   |
| `Move` / `Tx`  | `StockMovement`            |
| `Job` / `Work` | Use a domain-specific noun |

## Workflow when introducing a new concept

1. Read [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md) once if the concept is novel.
2. Read the relevant `docs/domains/<context>.md` to match vocabulary already in use in neighbour contexts.
3. Run the five-question checklist above. If anything fails, propose the qualified alternative in chat before writing code.
4. Apply the chosen name **consistently** in the same change across:
   - Prisma model + migration
   - `TENANT_SCOPED_MODELS` (if applicable)
   - Event registry + Zod payload schema
   - Command / handler / controller / DTO
   - Shared types
   - SDK method
   - Web UI labels and links
   - `docs/domains/<context>.md` and any Notion page
5. If the change introduces a new permanent convention, update [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md) in the same PR.

## When the user proposes a name

If the user proposes a name (e.g. "let's call it `Route`"), don't silently accept. Run the checklist, and if it fails, briefly state the conflict and propose a qualified alternative. Examples:

- User: "let's call the model `Route`."
  Response: "`Route` collides with Next.js/HTTP routing and reads ambiguously in the SDK (`createRoute()`). Proposing `DeliveryRoute` (with `DeliveryRouteStop`, `DispatchHandoff`, `DELIVERY_ROUTE_CREATED`) to match the explicit style we already use for `StockTransfer` and `PickingTask`."

- User: "add a `Task` model in warehouse."
  Response: "`Task` is too generic across warehouse/jobs/queues. Proposing `PickingTask` (or `PackingTask`, `StagingTask` depending on intent) so it reads clearly in `packages/types` and the SDK."

Only proceed once the user has confirmed the qualified name or explicitly overridden the recommendation.

## Anti-patterns

- Introducing `Route` / `Dispatch` / `Task` / `Item` / `Transfer` without a qualifier.
- Using a different name in the model, command, event, and UI for the same concept.
- Shipping a Prisma migration with a name we haven't yet validated in the SDK / web UI surface.
- Updating the model name but leaving the docs (`docs/domains/<context>.md`) and Notion with the old term.

## Reference

- [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md) - canonical rules and locked vocabulary per domain.
- [`docs/domains/`](../../../docs/domains/) - per-domain ownership and current vocabulary.
- [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md) - context boundaries and event flow.
