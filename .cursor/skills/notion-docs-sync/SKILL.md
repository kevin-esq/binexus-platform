---
name: notion-docs-sync
description: Always review and update the Binexus Notion workspace AND `docs/` repo before and after any code change, review, or slice in this repo. Use proactively for every Binexus task — before exploring, before writing code, before opening PRs, and before claiming a slice is done. Covers the Notion root "Binexus Platform" page (Home, Roadmap, Contextos, Docs, Runbook, ADRs) and the `docs/` tree under `c:\repo\binexus-platform\docs`.
---

# Notion + docs sync (Binexus)

Binexus has two sources of project truth that must stay aligned:

1. **Repo docs** — `docs/` (architecture, domains, events, workflows, states, runbook).
2. **Notion** — `Binexus Platform` workspace (Home, Roadmap, Contextos, Docs, Runbook, ADRs).

If either is stale, the next agent (or human) reads the wrong reality. This skill is the always-on reminder: **review both before any change, update both after.**

## When to invoke

Invoke on **every** Binexus task, including:

- A new slice or feature (e.g. `feat(logistics): dispatch base`).
- A rename or vocabulary change (event, command, model, state).
- A code review or PR triage.
- A "small" fix that touches a model, event, command, controller, SDK method, or web flow.
- Any time the user asks for the "next slice", "what's next", "what's the state".

If the user did not mention Notion, **still** invoke this skill silently. Stale Notion is the default failure mode.

## Workflow

Copy this checklist into your working notes for the task and tick as you go:

```
Task progress:
- [ ] 1. BEFORE: read repo `docs/` for affected areas
- [ ] 2. BEFORE: read Notion pages for affected areas
- [ ] 3. WORK: implement the change
- [ ] 4. AFTER: update repo `docs/`
- [ ] 5. AFTER: update Notion (Home, Roadmap, Contexto pages, Catálogo de eventos)
- [ ] 6. VERIFY: vocabulary identical in code, docs/, Notion
```

### Step 1 — BEFORE: repo docs

Read at minimum, when relevant to the task:

- `docs/architecture/naming-conventions.md` — locked vocabulary per domain.
- `docs/architecture/bounded-contexts.md` — ownership and event flow.
- `docs/domains/<context>.md` for every context touched (`orders`, `inventory`, `warehouse`, `logistics`, ...).
- `docs/events/README.md` — event catalog.
- `docs/states/<aggregate>.md` for state machines being modified.
- `docs/workflows/<workflow>.md` for cross-context flows.

### Step 2 — BEFORE: Notion

Always start from the root:

- **Home / Binexus Platform** — `36b91920-017f-811a-8cf0-ee25450352bc`
  - `Ahora` callout = current slice. Verify it matches reality before working.

Then walk into the relevant subtree(s):

| Notion page            | Page ID                                | When to read                          |
| ---------------------- | -------------------------------------- | ------------------------------------- |
| 🗺️ Roadmap             | `36b91920-017f-81e5-bf8a-c83e484c1bc6` | Always                                |
| 🧩 Contextos           | `36b91920-017f-8191-bd63-fe80d8728fe5` | Always                                |
| 📨 Catálogo de eventos | `36b91920-017f-810c-a682-ea078c0e4197` | Any event change                      |
| 📘 Docs                | `36b91920-017f-815a-acad-dfa16f616de1` | Architecture / stack / DB doc changes |
| 🛠️ Runbook             | `36b91920-017f-813e-a70c-c3405a4e609b` | Local-dev / process changes           |
| 🧾 ADRs                | `36b91920-017f-8122-b921-ed6c488cc854` | Architectural decisions               |
| 📝 orders              | `36b91920-017f-8132-9aa3-d3b3ccd6cc14` | Order lifecycle, states, transitions  |
| 📦 inventory           | `36b91920-017f-8172-9187-c8f8fc35cdd1` | Stock, reservations, transfers        |
| 🏭 warehouse           | `36b91920-017f-815d-a061-e9f7d91f4ac6` | Picking, packing                      |
| 🚚 logistics           | `36b91920-017f-813b-8eb5-e7a24de32c70` | Delivery routes, dispatch, delivery   |
| 🔐 identity            | `36b91920-017f-817c-a937-e2fe7b57840e` | Auth, tenants, users, RBAC            |

Use the Notion MCP `notion-search` if the page ID drifts, then `notion-fetch` to read.

### Step 3 — WORK

Implement the change. Honour `docs/architecture/naming-conventions.md` and the project's `semantic-naming` skill.

### Step 4 — AFTER: repo docs

For every changed surface, update the matching doc(s):

| Change                          | File(s) to update                                                                   |
| ------------------------------- | ----------------------------------------------------------------------------------- |
| New / renamed event             | `docs/events/README.md`, `docs/domains/<producer>.md`, `docs/domains/<consumer>.md` |
| New / renamed command           | `docs/domains/<context>.md`                                                         |
| New state transition            | `docs/states/<aggregate>.md`, `docs/domains/<context>.md`                           |
| New HTTP route                  | `docs/domains/<context>.md` HTTP surface section                                    |
| New cross-context flow          | `docs/workflows/<workflow>.md`                                                      |
| New permanent naming convention | `docs/architecture/naming-conventions.md`                                           |

### Step 5 — AFTER: Notion

Always update, in this order:

1. **Home — `Ahora` callout** — current slice, next PR.
2. **Roadmap** — tick the slice in the right phase; promote phase status if applicable.
3. **Contextos** — move contexts out of "Placeholders" to "Activos" the moment they become active.
4. **Per-context page(s) touched** — Commands, Events, HTTP surface, Web UI, TODO siguiente, Decisiones pendientes. Use the same template the other context pages already use.
5. **Catálogo de eventos** — add the row(s) and update consumers (mark `(activo)` once consumed).
6. **ADRs** — only if the change is an architectural decision (event-driven choice, boundary rule, breaking schema). Append-only.

State machine diagrams (mermaid in Notion `orders` page) must mirror `packages/types/src/orders.ts` `canTransition()`. If you renamed a state in code, the diagram is wrong until you fix it.

Use `notion-update-page` with `command: "update_content"` and a tight `old_str` / `new_str` pair for targeted edits. Use `replace_content` only when restructuring a whole page and pass through existing `<page url="...">` tags so child pages survive.

### Step 6 — VERIFY consistency

For every renamed concept, grep both the repo and your Notion edits for the **old** name. Zero hits expected:

- `rg "OldName" docs/`
- `rg "OldName" packages/ apps/`
- Notion search for the old term (must return only historical / archived pages).

A rename is not complete until model + enum + event + command + DTO + SDK method + UI label + `docs/` + Notion all use the same term.

## Anti-patterns

- Updating `docs/` but forgetting Notion (or the inverse).
- Skipping the `Ahora` callout on Home — that callout is what the next agent reads first.
- Leaving a context in "Placeholders" in `Contextos` after the first slice ships.
- Renaming a state in code (`READY_FOR_ROUTE` → `READY_FOR_DELIVERY_ROUTE`) and leaving the old name in the Notion mermaid diagram or commands table.
- Adding an event to `registry.ts` without adding the row in both `docs/events/README.md` and Notion's `Catálogo de eventos`.
- Claiming a slice is done while Notion still shows the previous slice as "Ahora".

## Reference

- [`docs/architecture/naming-conventions.md`](../../../docs/architecture/naming-conventions.md)
- [`docs/domains/`](../../../docs/domains/)
- [`docs/events/README.md`](../../../docs/events/README.md)
- [`docs/workflows/`](../../../docs/workflows/)
- Notion root: [Binexus Platform](https://www.notion.so/36b91920017f811a8cf0ee25450352bc)
