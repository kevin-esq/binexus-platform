---
name: claude-mem
description: Persistent cross-session memory + token-efficient structural code search for the Binexus repo via the `claude-mem` MCP server (which provides `smart_search`, `smart_outline`, `smart_unfold` AST tools). Use when a session needs to remember decisions across days, when re-exploring a large file would cost >5k tokens, or when you want AST-aware code search instead of `Grep`. Optional power-tool — install once via `npx claude-mem install`, then wire to `.cursor/mcp.local.json`.
---

# claude-mem (Binexus)

Two things in one package:

1. **Persistent memory** — keeps decisions, file states, and conclusions across Cursor sessions in a local knowledge graph. Complements [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md), which is in-session only.
2. **Smart explore MCP tools** — `smart_search`, `smart_outline`, `smart_unfold` use tree-sitter ASTs for token-efficient code navigation. Replaces the Grep → Glob → Read cycle for medium/large files. 4–18× token savings on common exploration patterns.

Upstream: https://github.com/thedotmack/claude-mem (npm: `claude-mem` v13.3.0, Apache 2.0).

## Why this skill is opt-in

- The MCP server is heavy (Bun runtime, persistent storage in `~/.claude-mem/`).
- The "memory" features are most valuable on multi-day projects with frequent context switches. For a single-session slice, `context-hygiene` is enough.
- It writes to `.cursor/mcp.json` (gitignored), which is **per-machine** state. Do not version.

Run the install only when you decide you want it.

## Verification status

- npm package resolvable: `claude-mem@13.3.0` ✓ (verified via `npm view`).
- `npx claude-mem` works (verified — prints install / runtime command help).
- The MCP server requires Bun. `npx claude-mem install` will install Bun in its plugin cache automatically.

## Install (one time, per machine)

```powershell
npx claude-mem install --ide cursor --provider openrouter
```

Flags:

- `--ide cursor` — target Cursor's MCP config.
- `--provider openrouter` — use whatever LLM provider you have an API key for. Other options: `claude`, `gemini`.

This will:

1. Install Bun + `uv` in `~/.claude-mem/`.
2. Write `.cursor/mcp.json` (already gitignored in this repo — safe).
3. Start a local worker service.

To skip auto-start: `npx claude-mem install --ide cursor --no-auto-start`.

To uninstall later: `npx claude-mem uninstall`.

## What you get inside Cursor

After install + restart, Cursor's agent gets three MCP tools. Prefer them over `Grep` / `Read` for code exploration in `apps/backend/` and `apps/web/`:

### `smart_search`

Find ranked symbols across a directory, plus folded file views, in one call.

```
smart_search(query="confirm delivery", path="./apps/backend/src/contexts/logistics")
```

Use cases in Binexus:

- "Where is the `DELIVERY_CONFIRMED` handler in Orders?" → `smart_search(query="DELIVERY_CONFIRMED", path="./apps/backend/src/contexts/orders")`.
- "Find all event handlers." → `smart_search(query="handler", path="./apps/backend/src/contexts")`.
- "Where do we call `forTenant()`?" → `smart_search(query="forTenant", path="./apps/backend/src")`.

### `smart_outline`

Structural skeleton of one file — functions, classes, methods, properties, imports. Replaces a full `Read` for files >100 lines.

```
smart_outline(file_path="apps/backend/src/contexts/logistics/application/commands/confirm-delivery.command.ts")
```

Token math: `smart_outline` ≈ 1-2k tokens vs full `Read` of a 535-line file ≈ 12k tokens.

### `smart_unfold`

Full source of ONE specific symbol (function, class, method) — JSDoc + decorators + body.

```
smart_unfold(file_path="apps/backend/src/contexts/logistics/application/commands/confirm-delivery.command.ts", symbol_name="ConfirmDeliveryHandler")
```

## When to prefer claude-mem tools over default tools

| Task                                             | Tool            | Cost                                                   |
| ------------------------------------------------ | --------------- | ------------------------------------------------------ |
| "Find all DELIVERY_CONFIRMED references"         | `smart_search`  | ~2-6k tokens (vs Glob+Grep+Read ~15-30k)               |
| "What's in confirm-delivery.command.ts?"         | `smart_outline` | ~1-2k vs Read ~12k                                     |
| "Show me ConfirmDeliveryHandler"                 | `smart_unfold`  | ~400-2k vs Read ~12k                                   |
| "Find all TODO comments"                         | `Grep`          | smart_search is overkill for plain regex               |
| "Read this 50-line config file"                  | `Read`          | smart_outline is overkill for tiny files               |
| "Synthesize how the entire logistics flow works" | `Task explore`  | needs cross-file narrative — smart\_\* is too granular |

Rule of thumb: code files >100 lines → smart\_\*. Plain text / config / docs → `Read`. Multi-file synthesis → `Task explore` subagent.

## The persistent memory side

Beyond smart\_\*, claude-mem stores session conclusions in a knowledge graph (`~/.claude-mem/db.sqlite`):

- Decisions made (e.g. "DELIVERY_CONFIRMED carries optional proof").
- File states (e.g. "ConfirmDeliveryHandler is idempotent on retry").
- Linked entities (commands, events, models).

These can be retrieved across sessions via the MCP tools (`mem_search`, `mem_recall`). Most useful for someone who works on this codebase daily; less useful for one-off contributors.

For Binexus, point claude-mem at the anchors enumerated in [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md) — model names, event names, command names. The MCP retains them across compactions so you don't lose them between sessions.

## Anti-patterns

- **Committing `.cursor/mcp.json`.** It contains tokens. The repo already gitignores it; do not whitelist.
- **Treating claude-mem memory as a docs replacement.** It is per-machine. `docs/` and Notion remain the durable record.
- **Using `smart_search` for tiny one-line questions** — `Grep` is faster.
- **Running `claude-mem install` inside CI.** It's a local power tool, not a build dependency.

## Disable temporarily

```powershell
npx claude-mem stop          # stop worker
npx claude-mem server stop   # stop MCP server
```

Restart: `npx claude-mem start && npx claude-mem server start`.

## Reference

- Upstream: https://github.com/thedotmack/claude-mem
- Smart-explore detail (the original skill text): [`skills/claude-mem-main/plugin/skills/smart-explore/SKILL.md`](../../../skills/claude-mem-main/plugin/skills/smart-explore/SKILL.md)
- [`.cursor/skills/context-hygiene/SKILL.md`](../context-hygiene/SKILL.md) — in-session counterpart
- [`.cursor/skills/learn-codebase/SKILL.md`](../learn-codebase/SKILL.md) — order in which to prime context (smart-explore reduces the cost of this step)
