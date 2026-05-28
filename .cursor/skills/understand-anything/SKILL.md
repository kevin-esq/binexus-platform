---
name: understand-anything
description: Generate an interactive knowledge graph of the Binexus codebase (files, functions, classes, dependencies, domain flows) for onboarding a new contributor, presenting the architecture to a stakeholder, or visualizing the impact of a large refactor. Use when the user asks to "visualize the codebase", "explain the architecture as a graph", "map the bounded contexts visually", "onboard a new dev", or "show how X connects to Y". Built on the upstream `Understand-Anything` tool; runs locally and renders a browser-based dashboard.
---

# understand-anything (Binexus)

Interactive code knowledge graph for the Binexus modular monolith. Designed for onboarding, architecture reviews, and refactor-impact analysis. Adapted from [Understand-Anything](https://github.com/Lum1104/Understand-Anything) (MIT).

## When to reach for it

- Onboarding a new developer to Binexus. Faster than `learn-codebase` because it's visual.
- Presenting architecture to a non-developer stakeholder (investor, compliance reviewer).
- Planning a cross-context refactor (e.g. "what does changing `ConfirmDeliveryHandler` touch downstream?").
- Building a guided tour through the bounded contexts for the team handbook.

Not for: in-session code reading (use [`smart-explore`](../claude-mem/SKILL.md) for that), production documentation (use `docs/` + Notion).

## What it produces

The upstream tool runs a multi-agent pipeline that:

1. Parses every source file (TypeScript, JavaScript, Python, etc.).
2. Extracts symbols + relationships (imports, calls, inheritance).
3. Builds a knowledge graph (force-directed, community-clustered).
4. Generates plain-English summaries for each node.
5. Generates a domain view: business processes laid out horizontally.
6. Generates guided tours ordered by dependency.

Output: an interactive HTML dashboard (`understand-anything-plugin/` folder) you open in a browser.

For Binexus specifically the domain view maps cleanly to bounded contexts: `identity`, `orders`, `inventory`, `warehouse`, `logistics`, `catalog`, `customers`, `sales`, `billing`, `reporting`. The graph view shows the event flow connecting them (`ORDER_APPROVED → INVENTORY_RESERVED → PICKING_COMPLETED → ...`).

## Install (one time)

The upstream installer targets several agent platforms but NOT Cursor directly. Two paths:

### Path A — Install for "vscode" (closest to Cursor)

```powershell
cd skills/Understand-Anything-main
./install.ps1 vscode
```

This clones the repo to `~/.understand-anything/repo` and creates symlinks at `~/.copilot/skills/`. Cursor does NOT auto-pick those up, but you can still run the analysis pipeline manually (the dashboard is a static HTML you open in any browser).

### Path B — Clone-only, manual run (recommended for Binexus)

The dashboard is the deliverable; the platform symlinks are not needed. Use the local clone we already have in `skills/Understand-Anything-main/`:

```powershell
cd skills/Understand-Anything-main
# Read README.md for the exact "analyze a codebase" command — it varies by version.
```

If the upstream evolves toward an `npx understand-anything` style invocation, prefer that — pin the version.

## Day-to-day use

After install, the typical flow:

1. Point the tool at the Binexus repo root.
2. Configure an LLM provider (OpenAI, Anthropic, OpenRouter — uses the LLM to summarize symbols and discover implicit relationships).
3. Wait for the analysis to complete (single-digit to tens of minutes depending on repo size).
4. Open the generated dashboard in a browser.
5. Navigate the graph (file view) or the domain view (bounded contexts).

The team can re-run on each major refactor to refresh the graph.

## Tuning for Binexus

When configuring the analysis:

- **Include**: `apps/backend/src/**`, `apps/web/src/**`, `packages/**/src/**`, `docs/**`.
- **Exclude**: `node_modules`, `dist`, `.turbo`, `.next`, `apps/backend/prisma/migrations` (history noise), `apps/backend/node_modules/.prisma` (generated Prisma client).
- **Domain hints**: feed the tool the list from [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md) as the domain seed.
- **Event seed**: feed the tool [`docs/events/README.md`](../../../docs/events/README.md) so the cross-context arrows match the real event bus.

## Output discipline

When sharing the generated dashboard:

- Host the static HTML in a private bucket (MinIO `binexus-internal-docs/`), not on a public URL.
- Title each generated dashboard with the commit SHA it was generated from.
- Treat it as a snapshot — not live documentation. The durable record stays in `docs/` + Notion.

## When NOT to use

- Mid-slice work — generating the graph costs minutes, an agent session breaks it.
- Convincing yourself a slice is "done" — write the tests instead.
- As a substitute for `docs/architecture/event-system.md`. The doc is the truth, the graph is a view.

## Reference

- Upstream README: [`skills/Understand-Anything-main/README.md`](../../../skills/Understand-Anything-main/README.md)
- Live demo from upstream: https://understand-anything.com/demo/
- Install script (Windows): [`skills/Understand-Anything-main/install.ps1`](../../../skills/Understand-Anything-main/install.ps1)
- [`.cursor/skills/learn-codebase/SKILL.md`](../learn-codebase/SKILL.md) — text-first counterpart for in-session priming
- [`.cursor/skills/zoom-out/SKILL.md`](../zoom-out/SKILL.md) — narrative architecture overview
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md) — durable architecture doc
