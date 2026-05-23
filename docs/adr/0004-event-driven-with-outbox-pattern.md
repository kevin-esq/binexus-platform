# ADR-0004: Event-driven communication with the Outbox pattern

| Field    | Value                                                  |
| -------- | ------------------------------------------------------ |
| Status   | Accepted                                               |
| Date     | 2026-05-23                                             |
| Deciders | Kevin Esquivel                                         |
| Tags     | architecture, events, reliability, outbox, integration |

## Context and problem statement

A workflow like "create order → reserve stock → schedule route → settle cash" spans Orders, Inventory, Logistics, and Billing. If we model these cross-context interactions with synchronous calls, the contexts become tightly coupled — Orders has to know how to talk to Inventory, Inventory has to know how to talk to Logistics, and a single transient failure cascades.

Worse: if a handler persists a row and _then_ tries to publish an event, a process crash between commit and publish silently loses the event. Receivers never see it; the system is permanently inconsistent.

**Question:** how do contexts communicate, and how do we never lose an event?

## Decision drivers

- **Decoupling** — adding Logistics must not require touching Orders.
- **Reliability** — an event committed to the DB must reach subscribers exactly-once-effectively (at-least-once delivery + idempotent consumers).
- **Replayability** — every event has an immutable record we can replay.
- **Migratable transport** — the in-process emitter we use today must be swappable for Redis Streams or Kafka later without touching producer code.
- **Audit for free** — events are the natural audit log (ADR coming, but design must support it now).

## Considered options

1. **Synchronous cross-context calls** — Orders directly invokes Inventory.
2. **Plain in-process event emitter** — fire events with `EventEmitter`, no persistence.
3. **Direct write to a broker** — Producer talks to Redis Streams / Kafka directly, no DB outbox.
4. **Outbox pattern + pluggable transport** — events persisted in the same DB transaction as the domain change, then dispatched asynchronously to a transport.

## Decision outcome

**Chosen option:** _Outbox pattern with a pluggable transport_. Events are persisted to an `OutboxEvent` table in the same transaction as the domain mutation. A dispatcher polls the table and publishes via an injectable `EVENT_TRANSPORT` (in-process today, Redis Streams later).

### Positive consequences

- **No lost events.** The event is part of the same transaction as the domain change — either both happen or neither does.
- **Crash-safe.** A process crash between domain write and event publish is recoverable: the dispatcher will retry.
- **Pluggable transport.** Switching from in-process to Redis Streams is a one-file change in `EventsModule`.
- **Built-in audit trail.** Every state change leaves a permanent envelope row.
- **Consumers stay idempotent** — the event `id` (ULID) is the dedupe key.

### Negative consequences

- **Eventual consistency between contexts.** Subscribers see the event slightly after the producer commits.
- **Outbox table grows.** Requires a retention/archive policy (Phase 1+).
- **Dispatcher is a new moving part** — must be monitored.

### Trade-offs accepted

- We give up "same-transaction synchronous" cross-context updates. Any handler that needs immediate cross-context coordination should be re-evaluated — it usually means the boundary is in the wrong place.
- Subscribers must be written to handle redelivery (idempotency). This is a constraint we _want_ anyway for offline-first.

## Pros and cons of the options

### Option 1 — Synchronous cross-context calls

- **Good:** Conceptually simplest.
- **Good:** Strong consistency.
- **Bad:** Tight coupling. Orders ends up knowing every downstream context.
- **Bad:** One slow consumer slows the whole request.
- **Bad:** Cascading failures.
- **Bad:** No audit trail unless we add one manually.

### Option 2 — Plain in-process event emitter

- **Good:** Trivial to implement.
- **Good:** Decouples senders from receivers.
- **Bad:** **Loses events on crash** — the deal-breaker.
- **Bad:** No persistence, no replay, no audit.

### Option 3 — Direct write to broker

- **Good:** No DB table to maintain.
- **Good:** Brokers are designed for durability.
- **Bad:** Dual-write problem: domain DB and broker are two separate systems. A crash between them loses or duplicates events.
- **Bad:** Two-phase commits are notoriously unreliable across heterogeneous systems.
- **Bad:** Wrong answer when you also want the event row as your audit log.

### Option 4 — Outbox + pluggable transport _(chosen)_

- **Good:** Single-transaction guarantee with the domain write.
- **Good:** Built-in audit trail.
- **Good:** Transport-agnostic.
- **Good:** Maps cleanly to future offline-first sync (outbox is exactly what we'd ship upstream from a hub).
- **Bad:** Extra dispatcher worker.
- **Bad:** Eventual delivery, not synchronous.

## Validation

This decision is working if:

- Every cross-context interaction goes through an event. No direct `someOtherContext.service.doThing()` calls.
- We can publish and consume an event end-to-end and inspect the envelope row in `OutboxEvent`.
- Switching `EVENT_TRANSPORT` from in-process to Redis Streams requires changing only `EventsModule`.
- The dispatcher's `publishedAt IS NULL AND createdAt < now() - 5m` query is permanently empty in steady state.

It is failing if:

- We catch handlers calling `eventBus.publish()` outside a `prisma.$transaction`.
- We add a "just this once, do it synchronously" handler that imports from another context's internals.
- Outbox table grows unbounded because no archive policy was implemented when it should have been.

## More information

- [Transactional Outbox — microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [Reliable Messaging Without Distributed Transactions — Pat Helland](https://queue.acm.org/detail.cfm?id=3199585)
- Related: ADR-0002 (modular monolith), ADR-0003 (offline-first), ADR-0007 (command bus)
- Related docs: [`docs/architecture/event-system.md`](../architecture/event-system.md), [`docs/events/README.md`](../events/README.md)
