# Binexus Platform — Documentation

This folder is the **single source of truth** for the platform's architecture, contracts, and operational reasoning. Every non-trivial decision lives here, not in commit messages.

## Two kinds of docs

| Kind                                                 | Where                                                           | Mutable?              |
| ---------------------------------------------------- | --------------------------------------------------------------- | --------------------- |
| **Architecture Decision Records** _(why?)_           | [`adr/`](adr/)                                                  | **No** — append-only. |
| **Living architecture docs** _(how it works today?)_ | `architecture/`, `domains/`, `states/`, `workflows/`, `events/` | Yes — kept current.   |

ADRs explain **why** we chose what we chose. Living docs explain **how the system works right now**. When the two disagree, the ADR is history and the living doc is the present.

## Reading order

1. [`adr/README.md`](adr/README.md) — the architectural decisions that shape everything below
2. [`architecture/overview.md`](architecture/overview.md) — start here for the runtime picture
3. [`architecture/bounded-contexts.md`](architecture/bounded-contexts.md)
4. [`architecture/multi-tenant.md`](architecture/multi-tenant.md)
5. [`architecture/event-system.md`](architecture/event-system.md)
6. [`architecture/audit-log.md`](architecture/audit-log.md)
7. [`architecture/commands.md`](architecture/commands.md)
8. [`architecture/observability.md`](architecture/observability.md)
9. [`architecture/feature-flags.md`](architecture/feature-flags.md)
10. [`architecture/dev-workflow.md`](architecture/dev-workflow.md) — branches, commits, PR, CI, rulesets
11. [`domains/README.md`](domains/README.md) — the 10 operational domains
12. [`states/order.md`](states/order.md) — order state machine
13. [`workflows/order-creation.md`](workflows/order-creation.md) — first end-to-end flow
14. [`events/README.md`](events/README.md) — event catalog

## How to add a new doc

- **New architectural decision**: add an ADR — see [`adr/README.md`](adr/README.md).
- **New bounded context or domain**: extend `domains/README.md` and add a per-domain page.
- **New state machine**: add to `states/<entity>.md` with a Mermaid diagram + transition table.
- **New event**: add to `events/README.md` + a Zod schema in `packages/events`.
- **Updating "how it works today"** because of a recent change: update the living docs in `architecture/`. Do NOT edit ADRs to reflect a new reality — write a new ADR that supersedes the old one.
