---
name: spec-kit
description: Spec-driven development for Binexus slices using GitHub's Spec Kit (`specify` CLI, installed locally). Use when a slice needs a real, executable specification before code — e.g. F7 Billing kickoff, a multi-context refactor, or a feature where requirements are unclear. Produces `/speckit.constitution → /speckit.specify → /speckit.plan → /speckit.tasks → /speckit.implement` workflow. Optional alternative to the existing `to-prd` + slice plan flow; use when the slice is larger than a single PR.
---

# spec-kit (Binexus)

Spec-driven development tooling for slice-larger-than-one-PR work. Wraps GitHub's [Spec Kit](https://github.com/github/spec-kit) `specify` CLI, already installed on this machine.

The Binexus default for normal slices remains: [`docs/`](../../../docs) + [`to-prd`](../to-prd/SKILL.md) + [`prompt-improver`](../prompt-improver/SKILL.md) + Plan Mode + Notion sync. Spec-kit is the heavier tool for the cases that justify it.

## When to reach for spec-kit instead of the normal flow

- A slice spans 3+ bounded contexts (e.g. Billing F7: Orders + Logistics + Billing + Reporting).
- Requirements are genuinely unclear and a stakeholder needs an executable spec they can review before code.
- A migration of a core invariant (e.g. switching from outbox to Redis Streams) where the spec acts as the migration plan.
- A new public-facing flow with external integration (e.g. Stripe Connect for tenants).

For everything smaller, stay on the slice cadence: `docs/domains/<context>.md` + Notion + PR template.

## Installation status

- `uv` 0.11.16 — installed (`C:\Users\Maria\.local\bin\uv.exe`, on user PATH).
- `specify` 0.8.17.dev0 — installed via `uv tool install specify-cli --from git+https://github.com/github/spec-kit.git`.
- Cursor agent integration — supported (`--ai cursor-agent`).
- Git — required and available.

Verify any time:

```powershell
specify check
specify version
```

If `specify` is missing after a shell restart, re-add the path: `$env:Path = "C:\Users\Maria\.local\bin;$env:Path"`. The user PATH already contains it, so this only matters in a fresh detached shell.

## Where spec-kit work lives in Binexus

Spec-kit creates a `.specify/` folder with templates and presets, plus a `specs/` folder with one spec per feature. Add both to the **per-spec** workflow without polluting the main monorepo:

```
specs/
├── <feature-slug>/        # one per feature
│   ├── spec.md            # the executable specification
│   ├── plan.md            # technical plan
│   ├── tasks.md           # actionable tasks
│   ├── constitution.md    # governing principles for this feature
│   └── research/          # supporting research
```

Recommended: keep `.specify/` and `specs/` at the repo root, NOT inside any package. Treat them like `docs/` — versioned, reviewable, but separate from runtime code.

## One-time init for the Binexus repo

Run only ONCE per repo, when a spec-kit workflow is about to start:

```powershell
specify init --here --ai cursor-agent --ai-skills --branch-numbering sequential
```

Flags:

- `--here` — initialize in the current dir (don't create a sub-folder).
- `--ai cursor-agent` — target Cursor's agent.
- `--ai-skills` — install Prompt.MD templates as agent skills under `.cursor/skills/`.
- `--branch-numbering sequential` — `001`, `002`, … matches Binexus's slice numbering style (F4-S1, F4-S2). Avoid timestamp branches.

After init, commit the `.specify/` and any new `.cursor/skills/speckit-*` folders in a separate PR titled `chore(specify): adopt spec-kit workflow`. Discuss with the team before running this — adopting spec-kit affects how slices are planned.

## Day-to-day commands

Inside a Cursor session, after spec-kit is initialized, the agent gets `/speckit.*` slash commands:

| Command                              | Purpose                                                                                         | Binexus mapping                                                                                                                   |
| ------------------------------------ | ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `/speckit.constitution <principles>` | Define governing principles for this feature (testing standards, multi-tenant guarantees, etc.) | Pull from [`docs/architecture/*.md`](../../../docs/architecture/) + [`.cursor/rules/*`](../../rules/)                             |
| `/speckit.specify <what + why>`      | Create the executable spec. Focus on WHAT and WHY, not the tech stack.                          | Pair with [`to-prd`](../to-prd/SKILL.md). Reference relevant `docs/domains/<context>.md` files.                                   |
| `/speckit.plan <tech stack>`         | Add tech stack + architecture choices.                                                          | Always state: NestJS, Prisma, Postgres, Redis, MinIO, multi-tenant via `forTenant()`, event bus via outbox.                       |
| `/speckit.tasks`                     | Break the plan into actionable tasks.                                                           | Maps to slices. Each task should fit a single PR.                                                                                 |
| `/speckit.implement`                 | Execute tasks.                                                                                  | Implementation pass per task; matches Binexus's [Plan → TDD → Review → Commit](../../rules/common-development-workflow.md) cycle. |

## How spec-kit interacts with Binexus rules

- **Constitution**: Always include the non-negotiables — multi-tenant isolation (`TENANT_SCOPED_MODELS`), event-bus outbox, command idempotency, Conventional Commits, Vitest coverage targets. Reference them by path in the constitution so they survive feature compression.
- **Specify**: Cite the relevant `docs/domains/<context>.md` and the Notion page for the bounded context. Spec-kit does not know about Binexus contexts unless you tell it.
- **Plan**: Honour [`semantic-naming`](../semantic-naming/SKILL.md) for every model / command / event introduced. Spec-kit will not enforce it; we do.
- **Tasks**: Each task ends with the same quality gates Binexus PRs already use (`turbo run typecheck lint build test`).
- **Implement**: Stays inside the slice cadence — small PRs, not one mega-PR.

## When NOT to use spec-kit

- Bug fixes — Cypress to bug fix flow, not spec-driven.
- Tiny slices (single file changes, copy edits, docs touches).
- UI iteration — use [`impeccable`](../impeccable/SKILL.md) + [`taste`](../taste/SKILL.md).
- Refactors with no behavior change — use [`improve-codebase-architecture`](../improve-codebase-architecture/SKILL.md).

## Reference

- Upstream: https://github.com/github/spec-kit
- Local install path: `C:\Users\Maria\.local\bin\specify.exe`
- [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md)
- [`.cursor/skills/to-prd/SKILL.md`](../to-prd/SKILL.md) — lighter PRD flow for slice-sized work
- [`.cursor/skills/prompt-improver/SKILL.md`](../prompt-improver/SKILL.md) — interactive scoping when the request is vague
- [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md) — keep specs and Notion in sync
