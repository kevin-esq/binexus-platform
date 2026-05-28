---
name: webapp-testing
description: Run end-to-end browser tests against the Binexus web app (`apps/web` on :3000) and backend (NestJS on :3001) with Playwright. Use when adding the first E2E for a flow, debugging a UI regression that unit tests can't catch, capturing screenshots / console logs for an ops bug, or verifying multi-tenant flows in the browser. Playwright is the chosen E2E tool but is not wired yet — this skill is the bootstrap and the day-to-day usage guide.
---

# webapp-testing (Binexus)

Browser-driven testing of `apps/web` against the running NestJS backend. Adapted from Anthropic's `webapp-testing` (Python/Playwright). Binexus is a TypeScript monorepo, so use `@playwright/test` in TS, not Python. Reference: [`skills/skills-mainb/skills/webapp-testing/SKILL.md`](../../../skills/skills-mainb/skills/webapp-testing/SKILL.md).

Aligns with [`.cursor/rules/typescript-testing.md`](../../rules/typescript-testing.md): **Playwright is the chosen E2E framework**; tests live in `apps/web/e2e/`.

## When to use

- First E2E for a new flow (e.g. confirm delivery → order delivered visible in `/orders`).
- A bug that depends on JS execution, network, or browser state and cannot be caught by Vitest.
- Capturing repro evidence (screenshot, video, network HAR) for an ops incident.
- Verifying multi-tenant isolation in the browser (login as tenant A, attempt to read tenant B).

Do NOT use Playwright for things Vitest does better:

- Pure unit tests of command handlers / SDK clients — use [`.cursor/rules/typescript-testing.md`](../../rules/typescript-testing.md) Vitest.
- API contract tests — call the SDK directly with `vitest`.

## Bootstrap (first time only)

If `apps/web/e2e/` does not exist yet, this is the one-time setup. Submit a separate PR titled `chore(web): add Playwright E2E bootstrap` so the wiring is reviewable on its own.

### 1. Install Playwright in `apps/web`

```bash
pnpm --filter @binexus/web add -D @playwright/test
pnpm --filter @binexus/web exec playwright install --with-deps chromium
```

Stick to Chromium until cross-browser coverage is justified.

### 2. Add `apps/web/playwright.config.ts`

Key choices:

- `testDir: './e2e'`.
- `webServer` array: one entry for `pnpm --filter @binexus/backend start:dev` on :3001, one for `pnpm --filter @binexus/web dev` on :3000.
- `use.baseURL: 'http://localhost:3000'`.
- `use.trace: 'on-first-retry'`, `use.screenshot: 'only-on-failure'`, `use.video: 'retain-on-failure'`.
- `forbidOnly: !!process.env.CI`, `retries: process.env.CI ? 2 : 0`.

### 3. Add scripts to `apps/web/package.json`

```json
"e2e": "playwright test",
"e2e:ui": "playwright test --ui",
"e2e:debug": "playwright test --debug"
```

### 4. Wire CI

Add an `e2e` job in [`.github/workflows/validate.yml`](../../../.github/workflows/validate.yml) that runs `pnpm --filter @binexus/web e2e` after `typecheck lint build`. Cache the Playwright browser download with `actions/cache`.

### 5. Seed a deterministic tenant

E2E needs known data. Use [`apps/backend/prisma/seed.ts`](../../../apps/backend/prisma/seed.ts) and an `e2e` Vitest tag, OR add `apps/web/e2e/fixtures/tenant.ts` that calls the seed via `pnpm --filter @binexus/backend prisma:seed -- --tenant=e2e`. Tests must NEVER assume prod-like state.

## Day-to-day usage

### Run

```bash
pnpm --filter @binexus/web e2e                  # headless, all tests
pnpm --filter @binexus/web e2e:ui               # Playwright UI runner
pnpm --filter @binexus/web e2e -- logistics     # single file by name
pnpm --filter @binexus/web e2e -- --grep "confirm delivery"
```

### Test file shape

```ts
import { test, expect } from '@playwright/test';

test.describe('confirm delivery', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill('e2e@acme.test');
    await page.getByLabel('Password').fill('e2e-password');
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page).toHaveURL(/\/orders/);
  });

  test('dispatcher confirms a stop and order becomes DELIVERED', async ({ page }) => {
    await page.goto('/logistics');
    await page
      .getByRole('button', { name: /confirm delivery/i })
      .first()
      .click();
    await expect(page.getByText('DELIVERED')).toBeVisible();
  });
});
```

### Reconnaissance → action

For dynamic routes, **wait for `networkidle`** before asserting:

```ts
await page.waitForLoadState('networkidle');
```

Do NOT poll with `page.waitForTimeout(...)`. Use `expect(locator).toBeVisible()` which auto-retries.

### Selectors, in order of preference

1. `getByRole('button', { name: /dispatch/i })`
2. `getByText('Confirm delivery')`
3. `getByLabel('Branch ID')`
4. `data-testid` only when no role / text / label fits. Add `data-testid` to the JSX in the same PR.

Avoid raw CSS / nth-child — those break on every layout tweak.

### Multi-tenant tests

Always login through `/login` against a known tenant seed. Never write to `localStorage` directly — go through the real auth flow so middleware, cookies, and `TenantContextService` headers all participate.

## Anti-patterns

- E2E tests that hit the production-style Notion or real MinIO buckets.
- Shared mutable state between tests (`order-1` reused everywhere). Each test creates its own.
- Asserting on raw HTML structure instead of accessible role / text.
- Skipping CI: an E2E suite that only runs locally rots within a month.
- Adding `page.waitForTimeout(2000)` — replace with an `expect` retry or `waitForLoadState`.

## Reference

- [`.cursor/rules/typescript-testing.md`](../../rules/typescript-testing.md) — Playwright is the chosen E2E tool, tests live in `apps/web/e2e/`.
- [`.cursor/skills/tdd/SKILL.md`](../tdd/SKILL.md) — TDD workflow; E2E sits at the outer ring of the test pyramid.
- [`apps/backend/prisma/seed.ts`](../../../apps/backend/prisma/seed.ts) — tenant + user seeds.
- Original skill (Python flavor): [`skills/skills-mainb/skills/webapp-testing/SKILL.md`](../../../skills/skills-mainb/skills/webapp-testing/SKILL.md).
