# ADR-0010: GitHub workflow — Modern Rulesets + CI fallback for pattern rules

| Field    | Value                                      |
| -------- | ------------------------------------------ |
| Status   | Accepted                                   |
| Date     | 2026-05-23                                 |
| Deciders | Kevin Esquivel                             |
| Tags     | github, ci, governance, branching, commits |

## Context and problem statement

We want a **modern, enforced** GitHub workflow on `main`:

- Linear history.
- Squash-merge only.
- Required status checks: lint, typecheck, build, test, CodeQL.
- No force pushes or deletions on `main`.
- Conventional Commits enforced.
- Branch names that follow `feat/*`, `fix/*`, `chore/*`, etc.
- Code-owner review on critical paths.

GitHub now ships **Repository Rulesets** as the modern replacement for legacy branch protection — they're versioned, JSON-defined, and exportable. We want to use them.

**But:** the repository is on a personal GitHub account on the **Free/Pro** plan. Some Ruleset rules — specifically `branch_name_pattern` and `commit_message_pattern` — are only available on **GitHub Team / Enterprise Cloud**. We discovered this empirically (`422 Validation Failed` from the API).

**Question:** how do we enforce the policy we want, given the plan limitation, without losing the modern Ruleset approach?

## Decision drivers

- **Modern, declarative, versioned governance** — Rulesets, not legacy branch protection.
- **Hard enforcement** — checks must block merges, not just warn.
- **Plan-portable** — moving to Team/Enterprise later should _strengthen_ enforcement, not require a rewrite.
- **Single source of truth** — the policy is in the repo, not in someone's browser tab.
- **Same checks run locally** — Husky hooks mirror what CI enforces.

## Considered options

1. **Legacy branch protection rules** (GitHub Classic Protection).
2. **Modern Rulesets with the unsupported pattern rules included** — accept the 422 errors and skip them.
3. **Modern Rulesets, no pattern rules in the ruleset; enforce patterns via required CI jobs + Husky locally** _(chosen)_.
4. **No enforcement** — convention only.

## Decision outcome

**Chosen option:** _Modern Rulesets, with pattern rules implemented as required CI jobs and locally as Husky hooks_.

Concretely:

- `.github/rulesets/main-protection.json` declares: required status checks, linear history, no force-push, no deletions, squash-only, required code-owner review on critical paths.
- The required status checks list includes **`Validate branch name`** and **`Validate commit messages`** — implemented as jobs in `.github/workflows/validate.yml`. They run on every PR and block merge on failure.
- Branch-naming and commit-message rules are also enforced locally via Husky (`commit-msg` hook + branch-name regex check) so contributors see failures before pushing.
- `scripts/apply-rulesets.ps1` / `.sh` apply the JSON declaratively to the repo via `gh api`.
- When the repo eventually moves to Team/Enterprise, we can move the pattern rules **into** the ruleset and **remove** the CI jobs — the policy stays unchanged, only the enforcement mechanism gets stronger.

### Positive consequences

- **Modern, declarative governance** — the policy lives in the repo as JSON.
- **No regression vs. Team/Enterprise** — every rule is enforced, just sometimes by Actions instead of by GitHub itself.
- **Local feedback** — Husky catches violations before the contributor pushes.
- **Plan-portable** — moving up plans is a removal of CI jobs, not a redesign.
- **Self-service onboarding** — anyone can re-apply the ruleset by running the script.

### Negative consequences

- **Two enforcement mechanisms** for branch/commit patterns (CI + Husky). Slight cognitive cost.
- **Required CI jobs must succeed** — a flaky Actions runner can block a merge. Mitigated by keeping the validate jobs cheap and stateless.
- **The pattern rules are _technically_ skippable** if a contributor disables their hook + uses `--no-verify` + pushes a branch directly (impossible against `main` thanks to the ruleset, but possible against `feat/*` branches before PR). The CI check still blocks the PR.

### Trade-offs accepted

- We accept slight duplication (CI + Husky) for the gain of immediate local feedback.
- We accept that some rules are "soft-enforced by convention + CI" rather than "hard-enforced by GitHub itself" until we move plans.

## Pros and cons of the options

### Option 1 — Legacy branch protection

- **Good:** Universally available.
- **Bad:** Not versioned in the repo.
- **Bad:** Configured per-rule in the UI — drift between repos is the default.
- **Bad:** Deprecated path forward.

### Option 2 — Rulesets with unsupported rules

- **Good:** Looks correct in the JSON.
- **Bad:** Doesn't apply — script fails with 422.
- **Bad:** Gives a false sense of enforcement.

### Option 3 — Rulesets + CI/Husky fallback _(chosen)_

- **Good:** Versioned, declarative.
- **Good:** Hard enforcement via required CI checks.
- **Good:** Local feedback via Husky.
- **Good:** Plan-portable upgrade path.
- **Bad:** Two enforcement mechanisms for two rules.

### Option 4 — No enforcement

- **Good:** Zero setup.
- **Bad:** Convention-only collapses the moment more than one person commits.

## Validation

This decision is working if:

- `pwsh -File scripts/apply-rulesets.ps1 -Action apply` succeeds and the ruleset is visible in the GitHub UI.
- A PR with a non-Conventional commit fails CI **and** fails locally on `git commit`.
- A PR titled with a non-Conventional title fails CI.
- A branch named `random-stuff` fails CI when opened as a PR.
- `main` cannot be force-pushed.

It is failing if:

- The validate workflow becomes a flake source.
- Contributors discover and routinely use `--no-verify`.
- We move to Team/Enterprise and forget to consolidate pattern rules into the ruleset (TODO: this ADR's "More information" links the migration checklist).

## More information

- [GitHub Repository Rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets)
- [Plan availability for branch / commit name patterns](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)
- [Conventional Commits](https://www.conventionalcommits.org/)
- Related docs: [`.github/rulesets/README.md`](../../.github/rulesets/README.md), [`docs/architecture/dev-workflow.md`](../architecture/dev-workflow.md), [`CONTRIBUTING.md`](../../CONTRIBUTING.md)
