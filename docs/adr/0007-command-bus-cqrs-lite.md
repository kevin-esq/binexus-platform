# ADR-0007: Command bus — CQRS-lite on `@nestjs/cqrs`

| Field    | Value                                   |
| -------- | --------------------------------------- |
| Status   | Accepted                                |
| Date     | 2026-05-23                              |
| Deciders | Kevin Esquivel                          |
| Tags     | architecture, cqrs, commands, use-cases |

## Context and problem statement

Binexus is **workflow-driven**: a customer creates an order, an approver approves it, a warehouse picks it, a router schedules it, a driver delivers it, accounting settles it. These are **use cases**, not CRUD operations. Modeling them as `OrdersController.update()` with a polymorphic body would:

- Bury the meaning of "approve an order" in branching code.
- Make cross-cutting concerns (validation, multi-tenant, audit, outbox) ad-hoc per endpoint.
- Make handlers untestable in isolation.

We need a place where every business use case is a **named, typed unit** with **one job**.

**Question:** what is the unit of business behavior on the backend, and how do controllers, validators, persistence, and events plug into it?

## Decision drivers

- **Each use case is its own object.** Easy to find, test, and reason about.
- **Same shape for every handler.** Validation, tenant scoping, persistence, and event publishing follow a single recipe.
- **No leaking domain logic into controllers.** Controllers should be translators between HTTP and commands.
- **Avoid full CQRS+ES weight.** We don't need event sourcing or separate read/write databases.
- **Standard library** — leverage `@nestjs/cqrs` instead of inventing our own bus.

## Considered options

1. **Service layer (`OrdersService.createOrder(...)`)** — classic NestJS, no bus.
2. **Anemic services + transaction scripts** — handlers as plain functions.
3. **Custom command bus** — homegrown registry, custom decorators.
4. **`@nestjs/cqrs` command bus + per-command handler classes** _(CQRS-lite: commands but no event sourcing)._
5. **Full CQRS + Event Sourcing** — commands, events as the source of truth, projections for reads.

## Decision outcome

**Chosen option:** _CQRS-lite on `@nestjs/cqrs`_. We use `CommandBus` and per-command handler classes for writes. Reads stay simple Prisma queries — no separate read model, no projections. We layer **our own** thin `AppCommand` envelope and `AppCommandHandler<TIn, TOut>` base type to enforce conventions (idempotency token, validation, outbox publishing).

The recipe each handler follows:

1. Validate the command payload (class-validator / Zod via a pipe).
2. Open a Prisma transaction inside the tenant-scoped client.
3. Perform the domain write.
4. Record one or more events via `OutboxService` **in the same transaction**.
5. Commit. The dispatcher (ADR-0004) publishes the events asynchronously.

### Positive consequences

- **Every use case is greppable.** `CreateOrderCommand` is one class, in one file.
- **Single recipe** for everything: handlers are boring in the best way.
- **Unit-testable handlers.** Mock `PrismaService` and `OutboxService`; assert the events that would be emitted.
- **No reinvention.** `@nestjs/cqrs` provides the bus; we provide the conventions.
- **Idempotency-friendly.** A `commandId` on every command makes "the network retried this" a no-op at handler entry.

### Negative consequences

- **More classes** than a flat service layer. Up-front cost for a 1-method use case.
- **`@nestjs/cqrs`'s `EventBus` is _not_** our event bus — we route domain events through `OutboxService` + `EVENT_TRANSPORT`. Two "event" concepts in the same DI container can confuse newcomers.
- **No magic batching** — sending two commands is two round-trips through the bus.

### Trade-offs accepted

- Some endpoints (pure reads) won't have a corresponding command — they're query handlers / direct Prisma calls. We tolerate that asymmetry; full CQRS-with-query-handlers everywhere is heavier than the read side justifies.
- We pay the cost of one class per use case in exchange for forever-future testability and discoverability.

## Pros and cons of the options

### Option 1 — Service layer

- **Good:** Familiar NestJS pattern.
- **Bad:** Encourages God-services (`OrdersService` with 30 methods).
- **Bad:** Cross-cutting concerns reimplemented per method.

### Option 2 — Anemic services + transaction scripts

- **Good:** Minimal classes.
- **Bad:** Domain logic scattered across files; hard to find "where do we approve an order?".
- **Bad:** Hard to test without standing up controllers.

### Option 3 — Custom command bus

- **Good:** Tailored exactly to our needs.
- **Bad:** Reinventing a wheel that `@nestjs/cqrs` ships maintained.
- **Bad:** Onboarding cost — new contributors must learn our bus, not the framework's.

### Option 4 — `@nestjs/cqrs` CQRS-lite _(chosen)_

- **Good:** Bus, decorators, and DI are first-class.
- **Good:** One class per use case = perfect grep-ability.
- **Good:** Pairs naturally with our outbox.
- **Bad:** Two "event" concepts in the same app (NestJS `EventBus` vs. our domain `EventBusService`).
- **Bad:** A bit ceremonious for trivial use cases.

### Option 5 — Full CQRS + Event Sourcing

- **Good:** Perfect audit, perfect replay, perfect time-travel.
- **Good:** Read/write models can scale independently.
- **Bad:** Massive Phase 0 investment.
- **Bad:** Projection rebuilds, schema migrations of the event log, and snapshot strategy are real engineering work.
- **Bad:** No team to absorb the operational weight today.

## Validation

This decision is working if:

- A new use case lands as a single file `apps/backend/src/contexts/<context>/commands/<name>.command.ts` + its handler + a controller line that calls `commandBus.execute(new XCommand(...))`.
- Handlers can be unit-tested in under 10 lines (mock `PrismaService` + `OutboxService`).
- Controllers contain only HTTP plumbing — no `if` branches on domain state.
- Cross-cutting concerns (validation, tenant scoping, outbox) are applied uniformly via base classes / interceptors, not re-implemented per handler.

It is failing if:

- We grow command handlers that call other command handlers directly (signal that we needed a process manager or a saga).
- We bypass `commandBus.execute()` and call handler classes directly from controllers (signal we don't trust the bus).
- A handler grows past ~150 lines (signal it's actually multiple use cases).

## More information

- [`@nestjs/cqrs` docs](https://docs.nestjs.com/recipes/cqrs)
- [Greg Young — CQRS Documents](https://cqrs.files.wordpress.com/2010/11/cqrs_documents.pdf)
- [Vaughn Vernon — Implementing DDD, Chapter 9 (Application services)](https://www.amazon.com/Implementing-Domain-Driven-Design-Vaughn-Vernon/dp/0321834577)
- Related: ADR-0002 (modular monolith), ADR-0004 (outbox), ADR-0005 (multi-tenant context).
- Related docs: [`docs/architecture/commands.md`](../architecture/commands.md)
