---
description: 'Git workflow for binexus-platform: Conventional Commits, Husky, commitlint, PR process'
alwaysApply: true
---

# Git Workflow

This repo enforces Conventional Commits, runs Husky + lint-staged on every commit, and validates PR titles in CI. Respect the gates; don't bypass them.

## Branch Naming

- `feat/<scope>-<short-description>` — new functionality
- `fix/<scope>-<short-description>` — bug fix
- `chore/<scope>-<short-description>` — tooling, infra, deps
- `docs/<scope>-<short-description>` — documentation only
- `refactor/<scope>-<short-description>` — code restructure without behaviour change

Always branch from `main` (or a sibling feature branch when explicitly stacking). Never commit directly to `main`.

## Commit Message Format

Conventional Commits enforced locally by `.husky/commit-msg` (commitlint) and in CI:

```
<type>(<scope>): <imperative subject ≤ 72 chars>

<body explaining WHY, not WHAT, wrapped at 100 chars>

<optional footers: Refs:, Closes:, BREAKING CHANGE:>
```

Allowed types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`, `ci`, `build`, `revert`.

Common scopes in this monorepo: `sdk`, `backend`, `web`, `desktop`, `events`, `types`, `config`, `ui`, `orders`, `identity`, `inventory`, `warehouse`, `logistics`, `sales`, `billing`, `commands`, `prisma`, `ci`, `deps`, `docs`, `foundation`.

### Examples

```
fix(sdk): avoid ReDoS in baseUrl trailing-slash trim
feat(orders): add CreateOrderCommand with outbox publish
chore(deps): bump @nestjs/core to 11.1.23
docs(adr): record decision to use modular monolith
```

### Forbidden

- Multiple unrelated changes in one commit
- `Co-authored-by` trailers added automatically by tooling (the user authors commits)
- `--no-verify` to skip hooks (CI also enforces, so it only delays the failure)

## Pre-Commit Hook Pipeline

`.husky/pre-commit` runs `lint-staged`, which:

1. Formats `*.{ts,tsx,js,jsx,json,md,yml,yaml}` with Prettier
2. Lints `*.{ts,tsx,js,jsx}` with ESLint --fix
3. Formats staged `apps/backend/**/*.cs` with `dotnet format … --include` (same gate as CI)

If lint-staged fails, fix the issue and re-stage; don't bypass with `--no-verify`.

## Commit-Msg Hook

`.husky/commit-msg` runs commitlint with `@commitlint/config-conventional`. Non-conventional messages are rejected locally.

## Pull Request Workflow

When opening a PR (this is the user's job, not the agent's):

1. Push branch with `-u origin <branch>` the first time
2. Open via `gh pr create` or the GitHub UI
3. The repo's PR template will load — fill the **Summary**, **Test plan**, and **Risk** sections
4. PR title must follow Conventional Commits (validated by `.github/workflows/pr-title.yml`)
5. Required CI gates: `Install`, `Typecheck`, `Lint`, `Build`, `Test`, `Validate`, `CodeQL`, `Prettier check`. All must be green before merge
6. Squash-merge to `main` (keeps history linear, preserves the PR title as the merge commit subject)

## Inspecting a PR Before Merge

```powershell
git diff main...HEAD --stat       # what files change
git log main..HEAD --oneline      # full commit list of the PR
git diff main...HEAD              # full content diff
```

## Force-Push Rules

- `--force-with-lease` is allowed on feature branches you own
- Never force-push to `main` — protected by ruleset
- Never force-push to a branch with an open PR if reviewers have already commented

## Agent Boundaries

- The agent NEVER runs `git commit` or `git push` on the user's behalf unless explicitly asked in the same turn
- The agent MAY run read-only commands: `git status`, `git diff`, `git log`, `git ls-remote`, `git fetch`
- When proposing a commit, hand the user the exact PowerShell command (this repo runs on Windows by default)
