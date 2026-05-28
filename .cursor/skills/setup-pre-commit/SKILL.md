---
name: setup-pre-commit
description: Wire Husky + lint-staged + Prettier + ESLint + Vitest into Binexus so every commit runs the local quality gates the CI runs. Use when the user asks to add pre-commit hooks, set up Husky, harden the commit workflow, stop bad commits from reaching CI, or fix existing hooks. Adapted to this repo's pnpm + Turborepo + Vitest + Prettier stack.
---

# setup-pre-commit (Binexus)

Set up local pre-commit hooks that mirror the CI checks in [`.github/workflows/validate.yml`](../../../.github/workflows/validate.yml) and the workflow described in [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md).

Adapted from `skills-main/skills/misc/setup-pre-commit`; full reference: [`skills/skills-main/skills/misc/setup-pre-commit/SKILL.md`](../../../skills/skills-main/skills/misc/setup-pre-commit/SKILL.md).

## What this sets up

- Husky v9+ `pre-commit` hook (no shebang required).
- `lint-staged` running Prettier on every staged file and ESLint on staged `*.{ts,tsx,js,jsx}`.
- Optional `commit-msg` hook validating Conventional Commits.
- A pre-push hook that runs a focused `turbo run typecheck lint test` on changed packages only.

Goal: catch what CI catches, locally, in seconds, without doubling commit time.

## Preconditions

- Package manager: **pnpm** (`pnpm-lock.yaml` is the source of truth).
- Workspace: Turborepo with `apps/backend`, `apps/web`, `packages/*`.
- Prettier and ESLint configs already live at the root and inside each package.
- Vitest is the test runner.

## Steps

### 1. Confirm scope with the user

Ask once:

- Install pre-commit + commit-msg? (recommended)
- Also install pre-push that runs `turbo run typecheck lint test` on affected packages?

Defaults: yes / yes.

### 2. Install dev dependencies at the workspace root

```bash
pnpm add -Dw husky lint-staged @commitlint/cli @commitlint/config-conventional
```

`-w` is critical — these are workspace-root tools, not per-package.

### 3. Initialize Husky

```bash
pnpm exec husky init
```

This creates `.husky/` and adds `"prepare": "husky"` to the root `package.json`. Verify the script was added.

### 4. Create `.husky/pre-commit`

```bash
pnpm exec lint-staged
```

Keep it to one line. Heavier checks belong in pre-push (step 7). If the repo enforces an SLA on commit time (rule: keep under ~5s), pre-commit must stay lint-staged-only.

### 5. Create `.lintstagedrc.json` at the repo root

```json
{
  "*.{ts,tsx,js,jsx}": ["eslint --fix", "prettier --write"],
  "*.{json,md,yml,yaml,prisma}": ["prettier --write"]
}
```

Notes:

- Do NOT add `*.sql` to Prettier — migrations are append-only and Prettier will reflow them.
- `*.prisma` is safe — Prettier with `prisma` plugin formats schema files.

### 6. Create `commitlint.config.cjs` at the repo root

```js
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'scope-empty': [2, 'never'],
    'scope-enum': [
      2,
      'always',
      [
        'identity',
        'orders',
        'inventory',
        'warehouse',
        'logistics',
        'sales',
        'billing',
        'reporting',
        'catalog',
        'customers',
        'foundation',
        'web',
        'sdk',
        'types',
        'events',
        'config',
        'ui',
        'docs',
        'ci',
      ],
    ],
  },
};
```

The `scope-enum` list maps 1:1 to the bounded contexts in [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md) plus packages and `docs`.

### 7. Create `.husky/commit-msg`

```bash
pnpm exec commitlint --edit "$1"
```

### 8. (Optional) Create `.husky/pre-push`

```bash
pnpm exec turbo run typecheck lint test --filter='[HEAD^]'
```

The `--filter='[HEAD^]'` tells Turbo to only run on packages changed since the previous commit. Falls back to a no-op on green if nothing changed. If this is too slow on your machine, drop `test` and rely on CI.

### 9. Verify

- [ ] `.husky/pre-commit`, `.husky/commit-msg`, optionally `.husky/pre-push` exist.
- [ ] `prepare` script in root `package.json` is `"husky"`.
- [ ] `.lintstagedrc.json` and `commitlint.config.cjs` exist.
- [ ] `pnpm exec lint-staged` runs cleanly on a staged file.
- [ ] A commit with subject `feat(logistics): test setup` passes commit-msg, a commit with subject `wip` fails.

### 10. Commit the change

Use the [`.github/pull_request_template.md`](../../../.github/pull_request_template.md) template. Suggested subject:

```
chore(ci): add husky + lint-staged + commitlint pre-commit hooks
```

## Out of scope

- Adding a `prisma format` hook — Prisma's CLI doesn't ship a fast incremental formatter; rely on Prettier's `prisma` plugin instead.
- Pre-commit Vitest on the full repo — would push commit time past the 5s SLA. Use pre-push instead.
- Sign-off / GPG signing automation — covered by [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md), not by Husky.

## Reference

- [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md)
- [`.cursor/rules/common-development-workflow.md`](../../rules/common-development-workflow.md)
- [`.github/workflows/validate.yml`](../../../.github/workflows/validate.yml)
- Original skill: [`skills/skills-main/skills/misc/setup-pre-commit/SKILL.md`](../../../skills/skills-main/skills/misc/setup-pre-commit/SKILL.md)
