# `.cursor/` — Project Guidance for Cursor Agents

This directory contains **project-wide guidance** for Cursor's AI agents working on the Binexus platform. It is intentionally versioned so every contributor's agent sessions follow the same conventions.

## What lives here

```
.cursor/
├── rules/   ← always-on or glob-scoped rules (style, security, testing, git, dev workflow)
└── skills/  ← on-demand skills the agent reads when relevant
```

### `rules/`

Markdown files with YAML frontmatter (`description`, `globs`, `alwaysApply`). Cursor injects them into the agent's context based on the frontmatter.

| File                             | Scope                                                          |
| -------------------------------- | -------------------------------------------------------------- |
| `common-coding-style.md`         | always                                                         |
| `common-development-workflow.md` | always — Plan → TDD → Review → Commit                          |
| `common-git-workflow.md`         | always — Conventional Commits, Husky, branch + PR rules        |
| `common-patterns.md`             | always — repository pattern, API envelope                      |
| `common-security.md`             | always — security checklist                                    |
| `common-testing.md`              | always — Vitest, TDD requirements                              |
| `typescript-coding-style.md`     | `**/*.{ts,tsx,js,jsx}`                                         |
| `typescript-patterns.md`         | `**/*.{ts,tsx,js,jsx}`                                         |
| `typescript-security.md`         | `**/*.{ts,tsx,js,jsx}` — ReDoS, Argon2, Prisma raw query rules |
| `typescript-testing.md`          | `**/*.{ts,tsx,js,jsx}` — Vitest / Playwright guidance          |

### `skills/`

Each skill is a folder with at least `SKILL.md`. The frontmatter `description` is what the agent reads to decide when to invoke the skill.

Workflow:

- `diagnose`, `tdd`, `grill-with-docs`, `improve-codebase-architecture`, `prototype`, `zoom-out`, `documentation-lookup`
- `to-prd`, `to-issues`, `triage`, `handoff`, `write-a-skill`

Stack-specific:

- `mcp-server-patterns`, `nextjs-turbopack`

Project conventions:

- `semantic-naming` — checks naming for new models, events, commands, shared types, SDK methods, and DTOs before generating code. Backed by [`docs/architecture/naming-conventions.md`](../docs/architecture/naming-conventions.md).

## What does NOT live here

These are gitignored (see root `.gitignore`):

- `.cursor/hooks/` and `.cursor/hooks.json` — runtime hook scripts that depend on per-machine state
- `.cursor/state/`, `.cursor/cache/`, `.cursor/.local/`, `.cursor/logs/` — runtime, ephemeral, personal
- `.cursor/mcp.json`, `.cursor/mcp.local.json` — MCP server config; can contain tokens or local paths

## Editing rules and skills

- Use the `write-a-skill` skill when authoring a new skill.
- Keep rules concise; agents pay context cost for everything in `alwaysApply: true`.
- Never put paths from your local machine, secrets, or personal tooling preferences here.
- Run `pnpm format` on this directory after editing — Prettier formats Markdown too.
