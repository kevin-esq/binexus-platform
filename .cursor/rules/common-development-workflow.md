---
description: 'Feature development workflow for binexus-platform: Plan → TDD → Review → Commit'
alwaysApply: true
---

# Development Workflow

The full pipeline before any commit. See `common-git-workflow.md` for what happens at commit time.

## 1. Plan First

Before writing code, write the plan:

- For trivial single-file edits, plan in the chat: 2-4 bullet steps
- For multi-file or cross-context changes, switch to **Plan Mode** in Cursor and produce a written plan
- For architectural decisions, write an ADR under `docs/adr/` using the template in `docs/adr/template.md`

A plan must answer: scope, files touched, contracts/types changed, risks, rollback story.

## 2. Test-Driven Where Practical

The `tdd` skill (`.cursor/skills/tdd/SKILL.md`) is the source of truth. Summary:

1. **RED** — write the test first, watch it fail meaningfully
2. **GREEN** — minimal implementation to make it pass
3. **REFACTOR** — clean up with the test as safety net

This repo uses **Vitest** for unit tests in `apps/backend` and `packages/*`. E2E will use **Playwright** when the web app gets real flows; not configured yet.

When TDD is not practical (UI prototypes, exploratory spikes, type-only refactors), document why in the commit/PR.

## 3. Review Your Own Code

Before saying "done" or opening the PR:

- Run the local gate: `pnpm lint && pnpm typecheck && pnpm test && pnpm build` (or scope to the changed package with `pnpm --filter <pkg> ...`)
- Read your own diff (`git diff`) once start-to-finish
- Check the `tdd`, `improve-codebase-architecture`, and `grill-with-docs` skills if the change deserves a deeper review

## 4. Commit and Push

See `common-git-workflow.md`. The agent does NOT run `git commit` or `git push` — hand the commands to the user.

## Mode Selection

Use Cursor's mode switcher proactively:

- **Plan Mode** — for ambiguous scope, multi-context changes, architectural choices
- **Agent Mode** — implementation with clear scope
- **Debug Mode** — runtime/test failures that need evidence-driven investigation
- **Ask Mode** — exploration, codebase questions, no writes

## Skill Activation

These bundled skills cover most workflows in this repo:

| Skill                           | When to invoke                                            |
| ------------------------------- | --------------------------------------------------------- |
| `diagnose`                      | Bug investigation, unexpected runtime behaviour           |
| `tdd`                           | New feature, bug fix needing regression test              |
| `grill-with-docs`               | Writing or updating ADRs, design docs                     |
| `improve-codebase-architecture` | Architectural review, refactor planning                   |
| `prototype`                     | Throwaway UI/logic spike to validate an idea              |
| `zoom-out`                      | Reset context after a long focused session                |
| `to-prd`                        | Turn an idea into a product requirement doc               |
| `to-issues`                     | Break a PRD or epic into trackable issues                 |
| `triage`                        | Move existing issues through the bug/enhancement workflow |
| `handoff`                       | Compact the session for another agent or for the user     |
| `write-a-skill`                 | Author a new skill in `.cursor/skills/`                   |
| `mcp-server-patterns`           | Build or extend an MCP server                             |
| `nextjs-turbopack`              | Work in `apps/web`                                        |
| `documentation-lookup`          | Resolve current behaviour of a 3rd-party library/API      |

Don't announce skill invocation — just read the skill and follow it.
