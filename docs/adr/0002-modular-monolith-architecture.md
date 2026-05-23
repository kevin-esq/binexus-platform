# ADR-0002: Modular monolith over microservices

| Field    | Value                                               |
| -------- | --------------------------------------------------- |
| Status   | Accepted                                            |
| Date     | 2026-05-23                                          |
| Deciders | Kevin Esquivel                                      |
| Tags     | architecture, runtime, deployment, bounded-contexts |

## Context and problem statement

Binexus is operational SaaS spanning identity, orders, inventory, sales, warehouse, logistics, billing, and reporting. There is real coupling between contexts — an order approves a credit limit, reserves stock, schedules a route, and later settles cash. The system _also_ needs to run in environments with poor connectivity, where a local hub serves a single branch and syncs to the cloud when it can.

Microservices would let teams scale independently — but we have one founder. They would also force every cross-context invariant (stock reservation, credit check, route assignment) through the network, where every failure becomes a distributed-systems problem.

**Question:** what runtime shape do we ship in?

## Decision drivers

- **Single founder, evolving domain** — bounded contexts will refactor as we learn the business.
- **Cross-context invariants must remain correctable** — we don't yet know all the consistency boundaries.
- **Offline-first** — fewer moving parts on a branch hub means fewer failure modes.
- **Future extractability** — we want to extract a service later without rewriting it.
- **Low ops cost** — one process to monitor, deploy, and roll back.

## Considered options

1. **Pure monolith** — one app, no internal boundaries.
2. **Modular monolith** — one app, hard internal boundaries (bounded contexts, in-process event bus, outbox).
3. **Microservices from day one** — one service per bounded context, networked via HTTP/gRPC or a broker.
4. **Serverless functions** — every command/endpoint as a function.

## Decision outcome

**Chosen option:** _Modular monolith_, because it gives us the operational simplicity of a single deployable while preserving the architectural seams we need to extract services later **only when each context's operational requirements diverge**.

### Positive consequences

- One process, one deploy, one log stream, one DB connection pool.
- Cross-context refactors are pure code changes — no contract negotiation between services.
- Local development is a single `pnpm dev` command.
- Offline hubs run the same artifact as cloud — no special build.

### Negative consequences

- Internal boundaries are enforced by **discipline + lint rules**, not by the network. Easy to violate accidentally.
- A bug in one context can take down all of them (mitigated by tests + observability).
- Horizontal scaling is coarse — we scale the whole monolith. Acceptable until any single context becomes the bottleneck.

### Trade-offs accepted

- Slower than microservices to "scale a hot context independently" — we'll cross that bridge by extracting that context (the modular layout makes it cheap).
- Some boundary violations will sneak in; we accept the cost of periodic audits (lint rules, dependency graphs, code review).

## Pros and cons of the options

### Option 1 — Pure monolith

- **Good:** Zero conceptual overhead.
- **Bad:** Code rot is the default outcome. Extracting anything later is a full rewrite.
- **Bad:** No way to enforce "Sales doesn't reach into Inventory's tables."

### Option 2 — Modular monolith _(chosen)_

- **Good:** Operational simplicity of a monolith.
- **Good:** Bounded contexts give us a real extraction surface later.
- **Good:** In-process event bus is fast and trivially debuggable; same envelope contract as the future Redis/Kafka transport.
- **Bad:** Boundaries require discipline to maintain.
- **Bad:** Single deployable means single failure domain.

### Option 3 — Microservices

- **Good:** Independent scaling, independent deploys, independent failure domains.
- **Good:** Forces hard contracts.
- **Bad:** Massive DevOps investment (service mesh, distributed tracing, schema registry, deploy pipelines per service).
- **Bad:** Every cross-context invariant becomes a saga.
- **Bad:** Offline hub topology becomes painful: do you run 9 services on a Raspberry Pi?
- **Bad:** Single founder cannot operate it.

### Option 4 — Serverless

- **Good:** Pay-per-use, scales to zero.
- **Good:** Forced statelessness pushes good design.
- **Bad:** Cold starts are unacceptable for POS UX.
- **Bad:** Offline hubs are not a serverless model.
- **Bad:** Vendor lock-in.
- **Bad:** Local development story is worse than monolith.

## Validation

This decision is working if:

- We can extract any one bounded context into its own service in **under a week**, with no domain logic changes.
- Boundary violations caught in code review are rare (≤ 1 per month).
- Deploy duration stays under 5 minutes for the whole monolith.

It is failing if:

- One context's traffic forces us to scale 8 unrelated contexts.
- We routinely import from `apps/backend/src/contexts/sales/...` inside `apps/backend/src/contexts/orders/...` (cross-context coupling).
- Domain logic ends up in the HTTP controllers because the modular layout was bypassed.

## More information

- [Modular Monoliths — Simon Brown](https://www.youtube.com/watch?v=5OjqD-ow8GE)
- [Monolith First — Martin Fowler](https://martinfowler.com/bliki/MonolithFirst.html)
- Related: ADR-0004 (event-driven + outbox), ADR-0005 (multi-tenant), ADR-0007 (command bus)
- Related docs: [`docs/architecture/bounded-contexts.md`](../architecture/bounded-contexts.md)
