---
name: graphify
description: Token-efficient code understanding for Binexus via an interactive knowledge graph (~71.5× reduction vs naive grep+read). Use when a session needs cross-file architecture context, when answering "how does X connect to Y", when reviewing the impact of a refactor, when onboarding a new contributor, or when the conversation keeps re-reading the same files. Backed by the upstream Graphify tool (installed; emits `graphify-out/graph.json` + `GRAPH_REPORT.md` + interactive `graph.html`). Pairs with the always-on rule at `.cursor/rules/graphify.mdc`.
---

# graphify (Binexus)

Knowledge-graph code understanding for Binexus. Combines tree-sitter AST extraction with LLM-driven semantic clustering to produce a queryable map of the modular monolith.

Upstream: [graphify.net](https://graphify.net/) · [GitHub](https://github.com/safishamsi/graphify) · MIT · Maintained by Safi Shamsi.

Local status (verified):

- `graphify` CLI installed (v0.8.22) via `pip install --user graphifyy` — `graphify --version` works.
- Required transitive deps installed manually (Graphify's PyPI metadata under-pins these):
  - `openai` (used by the `gemini` backend via its OpenAI-compatible API).
  - `networkx >= 3.0` (Graphify calls `nx.community.louvain_communities` introduced in NX 3.0; the PyPI metadata pulls 2.x by accident).
- Python user scripts dir added to user `Path`.
- Cursor integration applied: [`.cursor/rules/graphify.mdc`](../../rules/graphify.mdc) is `alwaysApply: true` — the agent always knows the graph is available.
- API key chosen: `GEMINI_API_KEY` (Google AI Studio, free tier). Stored in Windows User env vars; never committed.
- Initial graph built (1346 nodes / 2071 edges / 140 communities / build cost $0.0173).

## Why this matters for token economy

The Karpathy benchmark on the upstream site shows BFS subgraph queries costing ~1.7k tokens vs ~123k for naive read → ~71.5× reduction. For Binexus (`apps/backend/src` + `apps/web/src` + `packages/**` + `docs/**` ≈ 40-60k LoC at current state), the equivalent ratio is what we get back per architecture question.

This is in addition to [`context-hygiene`](../context-hygiene/SKILL.md) (in-session discipline) and [`claude-mem`](../claude-mem/SKILL.md) (smart_search/outline/unfold). The three layers compose:

| Layer           | Lifetime        | Cost                           | Best for                                                             |
| --------------- | --------------- | ------------------------------ | -------------------------------------------------------------------- |
| context-hygiene | in-session      | free                           | the anchors that must survive compaction                             |
| smart_explore   | per-call        | ~1-6k tokens / call            | "where is X" / "show me Y" / "outline this file"                     |
| graphify        | persisted graph | one-time build, then ~2k/query | "how does X connect to Y" / "what does this touch" / architecture qs |

## One-time build

The graph builds once, persists in `graphify-out/`, updates incrementally with `graphify update .`. The initial build IS LLM-driven and IS the one cost moment — after that, queries cost only tree-sitter + BFS (no LLM).

### Bootstrap from scratch (Binexus-tested)

```powershell
# 1) Install graphify + the deps graphify does NOT pull in automatically
python -m pip install --user graphifyy openai 'networkx>=3.0'

# 2) Add graphify to PATH (one-time)
$pyScripts = "C:\Users\$env:USERNAME\AppData\Local\Packages\PythonSoftwareFoundation.Python.3.12_qbz5n2kfra8p0\LocalCache\local-packages\Python312\Scripts"
[System.Environment]::SetEnvironmentVariable("Path", "$pyScripts;$([System.Environment]::GetEnvironmentVariable('Path','User'))", "User")
$env:Path = "$pyScripts;$env:Path"

# 3) Set the LLM provider key (User scope so it survives shells)
#    Free tier: Gemini via Google AI Studio (https://aistudio.google.com/app/apikey)
[System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "<your-AIza...-key>", "User")
$env:GEMINI_API_KEY = "<your-AIza...-key>"

# 4) Install the Cursor integration (adds .cursor/rules/graphify.mdc)
graphify install --platform cursor

# 5) Build the graph from the repo root
#    Use gemini-flash-latest, NOT gemini-1.5-flash (which Google has deprecated).
graphify . --backend gemini --model gemini-flash-latest

# 6) `graphify .` writes graph.json but does NOT auto-write graph.html or GRAPH_REPORT.md.
#    Run cluster-only to regenerate the human-readable outputs (no LLM cost):
graphify cluster-only .

# graphify-out/ now contains
#   ├── graph.html        (interactive visualization, ~1 MB)
#   ├── graph.json        (the queryable graph)
#   ├── GRAPH_REPORT.md   (god nodes + surprises + community map)
#   └── cache/            (incremental cache; safe to gitignore)
```

`graphify-out/` is gitignored (added separately). The graph is per-machine but small enough to share via the cache if needed.

### Model choice

`gemini-1.5-flash` is gone from the public API (deprecated by Google late 2025 / early 2026). Use one of:

- `gemini-flash-latest` — evergreen alias, points at the current production flash. **Recommended**: future-proof.
- `gemini-2.5-flash` — explicit, current latest flash.
- `gemini-2.5-flash-lite` — cheapest variant, slightly less accurate semantic extraction.
- `gemini-2.5-pro` — overkill for graphify's small extraction calls.

Free-tier quota (as of mid-2026): 15 RPM, 1M tokens/day on flash. Binexus's full build consumes ~6k input / 5k output tokens — fits in free tier with three orders of magnitude to spare.

## Day-to-day queries

After the build, the always-on Cursor rule will steer the agent to use these instead of `Grep` for architecture questions:

```powershell
graphify query "how is multi-tenancy enforced across contexts"
graphify explain "TenantContextService"
graphify path "ConfirmDeliveryCommand" "DeliveryConfirmedOrdersHandler"
```

What each returns:

- `query` — BFS subgraph traversal answering a natural-language question. ~2k tokens.
- `path` — shortest path between two nodes. Shows the chain between them.
- `explain` — plain-language explanation of a node + its immediate neighbors.

### Naming gotcha — events vs handlers

Domain event NAMES (`ORDER_DELIVERED`, `DELIVERY_CONFIRMED`, `INVENTORY_RESERVED`, …) are **string literals in code** and therefore NOT distinct nodes. Graphify only creates nodes for declared TypeScript identifiers + Markdown headings.

To query "what consumes event X", use the **handler file name**, not the event name:

| You want to query for...              | Use this node name instead                                                      |
| ------------------------------------- | ------------------------------------------------------------------------------- |
| `ORDER_DELIVERED` consumers           | `delivery-confirmed.handler.ts` / `DeliveryConfirmedOrdersHandler`              |
| `INVENTORY_RESERVED` consumers        | `inventory-reserved.handler.ts` / `InventoryReservedOrdersHandler`              |
| `DELIVERY_ROUTE_DISPATCHED` consumers | `delivery-route-dispatched.handler.ts` / `DeliveryRouteDispatchedOrdersHandler` |
| `PICKING_COMPLETED` consumers         | `picking-completed.handler.ts`                                                  |

The event payload zod schemas (`deliveryConfirmedPayload`, `inventoryReservedPayload`, …) ARE nodes — useful when querying about the schema shape itself.

For Binexus this is especially powerful on cross-context questions:

| Question                                                      | Without graphify                   | With graphify                              |
| ------------------------------------------------------------- | ---------------------------------- | ------------------------------------------ |
| "What does `DELIVERY_CONFIRMED` touch downstream?"            | Glob + Grep + Read ≥ 15-30k tokens | `graphify query` ~2k                       |
| "Path from `CreateOrderCommand` to `ORDER_DELIVERED`?"        | Manual trace through 4 contexts    | `graphify path`                            |
| "Where is `forTenant` used and how does it relate to outbox?" | Grep across 50 files               | `graphify explain TenantContextService`    |
| "What's the highest-impact node in Logistics?"                | Read every file in the context     | `graphify-out/GRAPH_REPORT.md` "god nodes" |

## Keeping the graph current

The graph drifts as the code changes. Cheap refresh after edits:

```powershell
graphify update .          # AST-only; no LLM cost
```

Heavier refresh (re-extracts semantic relationships via LLM):

```powershell
graphify .                  # full rebuild
```

Run `graphify update .` after every slice. Run a full rebuild once per phase (F4 → F5 → F6 etc.) or when the docs in `docs/architecture/` change substantively.

### Live mode (optional)

```powershell
graphify watch .
```

Watches the repo and auto-rebuilds (AST-only) on file changes. Useful during big refactors. Run in a separate terminal.

## Configure scope

`graphify` does not need explicit include/exclude — it auto-detects the language and respects `.gitignore`. For Binexus this means:

- Included by default: `apps/backend/src/**`, `apps/web/src/**`, `packages/**/src/**`, `docs/**`.
- Excluded by gitignore: `node_modules`, `dist`, `.next`, `.turbo`, `coverage`, `prisma/migrations/dev.db*`, the vendored `skills/` directory.

If you ever want to include a single sub-tree to keep token cost down for a phased build:

```powershell
graphify ./apps/backend/src/contexts/logistics
```

That produces a context-local graph in `graphify-out/`. Useful when you only need the Logistics view, not the whole monolith.

## What to do with `GRAPH_REPORT.md`

The report is short and high-signal. After every full rebuild, scan:

- **God nodes** — the most-connected entities. Should map to the aggregate roots (`DeliveryRoute`, `Order`) and the cross-cutting services (`PrismaService`, `TenantContextService`, `EventBus`). If a non-architectural file shows up here, that's a code smell.
- **Surprises** — unexpected edges. Genuine architectural surprises (e.g. Logistics importing from Identity) should be questioned in a PR.
- **Suggested questions** — auto-generated. Often a good first read after onboarding.

## Multi-repo (when sub-apps split)

When the driver app lands as `apps/mobile/` and shares `@binexus/types` / `@binexus/sdk`, build both graphs and merge:

```powershell
graphify apps/backend
graphify apps/mobile
graphify merge-graphs apps/backend/graphify-out/graph.json apps/mobile/graphify-out/graph.json --out graphify-out/merged-graph.json
```

This gives a single cross-app view for security / refactor planning.

## Git merge driver (optional)

Avoid merge conflicts on `graph.json` when two branches both regenerate:

```powershell
graphify merge-driver <base> <current> <other>   # union-merge two graph.json files
```

Set it up once via `git config`. Documented in upstream README.

## Privacy / cost guarantees from upstream

- Graphify only sends **semantic descriptions of documents** to the LLM, never raw source code.
- It uses the model API key already configured for the assistant. No telemetry.
- URLs are restricted to http/https; downloads are size- and time-bounded.
- Output paths are containment-checked.

## Known Binexus god nodes (from the first build)

For reference when planning a refactor — these are the 10 most-connected nodes (touch with care):

| Rank | Node                              | Edges |
| ---- | --------------------------------- | ----- |
| 1    | `PrismaService`                   | 96    |
| 2    | `TenantContextService`            | 68    |
| 3    | `AppCommand`                      | 53    |
| 4    | `EventBusService`                 | 34    |
| 5    | `AppCommandMetadata`              | 33    |
| 6    | `OutboxService`                   | 33    |
| 7    | `AppCommandHandler`               | 31    |
| 8    | `OrdersModule`                    | 31    |
| 9    | `CreateDeliveryRouteResult`       | 27    |
| 10   | `AssignOrderToDeliveryRouteInput` | 27    |

Treat the top 8 as the core cross-cutting infrastructure of Binexus. The two `packages/types` entries (#9, #10) are interesting — Logistics shapes are widely re-exported.

## Anti-patterns

- Running `graphify .` inside CI without a token budget — it's an LLM-paid step.
- Committing `graphify-out/graph.json` to the repo. It's per-machine, regen is cheap.
- Treating the graph as docs. The graph helps the agent navigate; `docs/` + Notion remain the durable record.
- Updating only after months. Stale graph misleads. Run `graphify update .` per slice.
- Accepting "Surprising Connections" blindly. The LLM extractor occasionally confuses _usage_ edges for _inherits_ edges (e.g. it flagged several `packages/types/*` shapes as inheriting from `AppCommandHandler`, which they don't). Verify before acting.
- Forgetting `graphify cluster-only .` after a build — without it, `graph.html` and `GRAPH_REPORT.md` are stale or missing.

## Reference

- Always-on rule: [`.cursor/rules/graphify.mdc`](../../rules/graphify.mdc)
- Upstream: [graphify.net](https://graphify.net/)
- PyPI package: `graphifyy` (CLI: `graphify`)
- [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md) — in-session counterpart
- [`.cursor/skills/claude-mem/SKILL.md`](../claude-mem/SKILL.md) — smart_search / outline / unfold
- [`.cursor/skills/understand-anything/SKILL.md`](../understand-anything/SKILL.md) — alternative knowledge-graph approach (browser dashboard)
- [`docs/architecture/event-system.md`](../../../docs/architecture/event-system.md) — the durable counterpart of what the graph captures
