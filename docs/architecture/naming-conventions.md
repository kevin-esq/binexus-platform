# Naming conventions

Binexus is a multi-context modular monolith (.NET 10 + web packages). Names cross context boundaries through `@binexus/types`, `@binexus/sdk`, `apps/backend/contracts/events`, and the web UI, so they have to read clearly on their own. This document captures the rules we apply when introducing models, events, commands, or shared types. (`@binexus/events` was removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md).)

## Principles

1. **Be explicit, not clever.** A name should describe what the thing is in business terms. Prefer `DeliveryRoute` over `Route`, `StockTransfer` over `Transfer`, `PickingTask` over `Task`.
2. **Avoid framework collisions.** Names must not collide with web/framework concepts. `Route` collides with Next.js/HTTP routing. `Dispatcher` collides with the outbox dispatcher. Add the domain qualifier (`DeliveryRoute`, `DispatchHandoff`) instead of relying on context.
3. **Be consistent across contexts.** If `inventory` uses `StockTransfer` (explicit noun phrase), `logistics` should use `DeliveryRoute`, not `Route`. Vocabulary in one context sets expectations for the others.
4. **Reads cross-context-friendly.** Imagine the symbol on its own in the SDK or web UI: `client.createRoute()` is ambiguous, `client.createDeliveryRoute()` is not.
5. **One concept, one term.** Once a term is chosen, use it everywhere - model, command, event, DTO, controller path, UI label, docs. Do not silently introduce synonyms.

## Naming rules by artifact

| Artifact            | Convention                                    | Example                                          |
| ------------------- | --------------------------------------------- | ------------------------------------------------ |
| EF / domain model   | `PascalCase`, explicit business noun          | `StockTransfer`, `PickingTask`, `DeliveryRoute`  |
| Enum                | `PascalCase` name, `SCREAMING_SNAKE` values   | `enum StockTransferStatus { PENDING, ... }`      |
| Command class       | `<Verb><Noun>Command`                         | `CreateStockTransferCommand`                     |
| Command handler     | `<Verb><Noun>Handler`                         | `CreateStockTransferHandler`                     |
| Domain event name   | `SCREAMING_SNAKE`, past tense, qualified noun | `STOCK_TRANSFER_CREATED`, `PICKING_COMPLETED`    |
| Event payload type  | `<EventName>Payload` (PascalCase)             | `StockTransferCreatedPayload`                    |
| HTTP route          | Plural, kebab-case, qualified noun            | `POST /inventory/stock/transfers`                |
| Shared type (types) | `PascalCase`, qualified noun                  | `StockTransferSummary`, `PickingTaskSummary`     |
| SDK method          | `<verb><Noun>()`                              | `createStockTransfer()`, `completePickingTask()` |

## Banned / risky names (use qualified alternatives)

| Avoid          | Why                                             | Use instead                |
| -------------- | ----------------------------------------------- | -------------------------- |
| `Route`        | Collides with HTTP / Next.js routing            | `DeliveryRoute`            |
| `Dispatch`     | Collides with outbox dispatcher                 | `DispatchHandoff`          |
| `Transfer`     | Too generic across stock/payments/routes        | `StockTransfer`            |
| `Task`         | Too generic across warehouse/jobs/queues        | `PickingTask`              |
| `Item`         | Too generic across stock/order/cart             | `StockItem`, `OrderLine`   |
| `Move` / `Tx`  | Ambiguous with framework transactions/movements | `StockMovement`            |
| `Job` / `Work` | Too generic                                     | Use a domain-specific noun |

## Process when introducing a new concept

When adding a model, command, event, or shared type:

1. Write the candidate name.
2. Ask: does this collide with any framework concept (`Route`, `Dispatch`, `Handler`, `Service`, `Module`, `Controller`)? If yes, qualify it.
3. Ask: read on its own in `packages/types` or the SDK, is the business meaning still obvious? If not, qualify it.
4. Ask: is there an existing context already using a similar pattern (e.g. `StockTransfer`)? Match its level of explicitness.
5. Update the relevant docs (`docs/domains/<context>.md`, this file if a new convention emerges) in the same change.

## Cross-context enum values

Enum values that other contexts read (especially the `OrderState` state machine in `packages/types/src/orders.ts`) follow the same rules. A bare `READY_FOR_ROUTE` reads ambiguously next to `PICKING`/`DELIVERED` and breaks consistency once Logistics ships `DeliveryRoute`. Use the qualified form (`READY_FOR_DELIVERY_ROUTE`) so the state machine, events emitted on transition (`ORDER_READY_FOR_DELIVERY_ROUTE`), and any UI label stay aligned with the Logistics vocabulary.

## Logistics vocabulary (locked)

Logistics is the context where naming collisions are highest, so the vocabulary is fixed here:

- `DeliveryRoute` (not `Route`)
- `DeliveryRouteStop` (not `RouteStop`)
- `DispatchHandoff` (not `Dispatch`)
- `DeliveryProof`
- `DeliveryRouteLiquidation` (not `RouteLiquidation`)
- Events: `DELIVERY_ROUTE_CREATED`, `DELIVERY_ROUTE_ASSIGNED`, `DELIVERY_ROUTE_DISPATCHED`, `DELIVERY_CONFIRMED`, `DELIVERY_FAILED`, `DELIVERY_ROUTE_LIQUIDATED`.
- Order state machine: `OrderState.READY_FOR_DELIVERY_ROUTE` (not `READY_FOR_ROUTE`), command `MarkOrderReadyForDeliveryRouteCommand`, future event `ORDER_READY_FOR_DELIVERY_ROUTE`.

This applies before Logistics is implemented so that the first migration, types, and SDK methods land with the final names.
