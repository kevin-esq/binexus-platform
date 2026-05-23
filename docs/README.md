# Binexus Platform — Documentation

This folder is the **single source of truth** for the platform's architecture, contracts, and operational reasoning. Every non-trivial decision lives here, not in commit messages.

## Reading order

1. [`architecture/overview.md`](architecture/overview.md) — start here
2. [`architecture/bounded-contexts.md`](architecture/bounded-contexts.md)
3. [`architecture/multi-tenant.md`](architecture/multi-tenant.md)
4. [`architecture/event-system.md`](architecture/event-system.md)
5. [`architecture/commands.md`](architecture/commands.md)
6. [`architecture/observability.md`](architecture/observability.md)
7. [`architecture/feature-flags.md`](architecture/feature-flags.md)
8. [`architecture/dev-workflow.md`](architecture/dev-workflow.md) — branches, commits, PR, CI, rulesets
9. [`domains/README.md`](domains/README.md) — the 7 domains
10. [`states/order.md`](states/order.md) — order state machine
11. [`workflows/order-creation.md`](workflows/order-creation.md) — first end-to-end flow
12. [`events/README.md`](events/README.md) — event catalog

## How to add a new doc

- Architectural decision (ADR-ish): add to `architecture/` and link it from `overview.md`.
- New bounded context or domain: extend `domains/README.md` and add a per-domain page.
- New state machine: add to `states/<entity>.md` with a Mermaid diagram + transition table.
- New event: add to `events/README.md` + a Zod schema in `packages/events`.
