# Binexus Platform — Documentation

This folder is the **single source of truth** for the platform's architecture, contracts, and operational reasoning. Every non-trivial decision lives here, not in commit messages.

## Two kinds of docs

| Kind                                                 | Where                                                           | Mutable?              |
| ---------------------------------------------------- | --------------------------------------------------------------- | --------------------- |
| **Architecture Decision Records** _(why?)_           | [`adr/`](adr/)                                                  | **No** — append-only. |
| **Living architecture docs** _(how it works today?)_ | `architecture/`, `domains/`, `states/`, `workflows/`, `events/` | Yes — kept current.   |

ADRs explain **why** we chose what we chose. Living docs explain **how the system works right now**. When the two disagree, the ADR is history and the living doc is the present.

## Reading order

1. [`adr/README.md`](adr/README.md): architectural decisions that shape everything below
2. [`architecture/overview.md`](architecture/overview.md): start here for the runtime picture
3. [`architecture/branch-runtime.md`](architecture/branch-runtime.md): proposed Branch Runtime topology
4. [`architecture/desktop-tauri.md`](architecture/desktop-tauri.md): proposed desktop shell boundaries
5. [`architecture/branch-wizard-ux.md`](architecture/branch-wizard-ux.md): proposed first-run branch setup flow
6. [`architecture/web-vs-desktop-surfaces.md`](architecture/web-vs-desktop-surfaces.md): proposed surface split
7. [`architecture/bounded-contexts.md`](architecture/bounded-contexts.md)
8. [`architecture/multi-tenant.md`](architecture/multi-tenant.md)
9. [`architecture/event-system.md`](architecture/event-system.md)
10. [`architecture/audit-log.md`](architecture/audit-log.md)
11. [`architecture/commands.md`](architecture/commands.md)
12. [`architecture/observability.md`](architecture/observability.md)
13. [`architecture/feature-flags.md`](architecture/feature-flags.md)
14. [`architecture/dev-workflow.md`](architecture/dev-workflow.md): branches, commits, PR, CI, rulesets
15. [`domains/README.md`](domains/README.md): the 10 operational domains
16. [`states/order.md`](states/order.md): order state machine
17. [`workflows/order-creation.md`](workflows/order-creation.md): first end-to-end flow
18. [`events/README.md`](events/README.md): event catalog
19. [`migration/local-setup.md`](migration/local-setup.md): .NET local boot and clean DB recreate

## How to add a new doc

- **New architectural decision**: add an ADR — see [`adr/README.md`](adr/README.md).
- **New bounded context or domain**: extend `domains/README.md` and add a per-domain page.
- **New state machine**: add to `states/<entity>.md` with a Mermaid diagram + transition table.
- **New event**: add to `events/README.md` + a contract under `apps/backend/contracts/events` ([ADR-0015](adr/0015-nestjs-retirement-dotnet-sole-backend.md)).
- **Updating "how it works today"** because of a recent change: update the living docs in `architecture/`. Do NOT edit ADRs to reflect a new reality — write a new ADR that supersedes the old one.

**Backend (active):** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL. NestJS is not supported. Legacy: NestJS, removed in ADR-0015.
