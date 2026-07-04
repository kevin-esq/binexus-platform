---
description: 'TypeScript/JavaScript testing extending common rules'
globs: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx']
alwaysApply: false
---

# TypeScript/JavaScript Testing

Extends `common-testing.md` with stack-specific guidance for this monorepo.

## Frameworks

- **Vitest** — unit and integration tests in `apps/backend` and `packages/*`. Config lives in each package; the test script is `vitest run --passWithNoTests`.
- **Playwright** — chosen for E2E in `apps/web` (not wired yet — add when the first real flow exists).
- **Prisma test isolation** — use a transactional setup or a per-suite schema; never share DB state across tests.

## Where Tests Live

- Co-located next to the unit under test: `feature.ts` + `feature.spec.ts`.
- Integration tests that need real infrastructure (MinIO, Postgres, Redis): `apps/backend/src/__integration__/` — **excluded** from the default unit run; run via `pnpm test:integration` (sets `INTEGRATION=1` via `vitest.integration.config.ts`).
- E2E tests: `apps/web/e2e/` (when added).

## Unit vs integration runs

| Command                 | Scope                                                             | Config                                      | Requires                    |
| ----------------------- | ----------------------------------------------------------------- | ------------------------------------------- | --------------------------- |
| `pnpm test`             | Unit specs only (`src/**/*.spec.ts`, excludes `__integration__/`) | `apps/backend/vitest.config.ts`             | Nothing extra               |
| `pnpm test:integration` | Integration specs only (`src/**/__integration__/**/*.spec.ts`)    | `apps/backend/vitest.integration.config.ts` | MinIO up (`pnpm docker:up`) |

Integration files are **not loaded** during `pnpm test` — Vitest excludes the folder entirely (not `describe.skip`).

Preflight: if MinIO is down, integration tests fail in `beforeAll` with:

```text
Integration tests require MinIO. Start it with: pnpm docker:up
Expected S3_ENDPOINT=http://localhost:9000. Health check failed: ...
```

Current integration coverage (slice #5): presigned proof upload → real PUT → HeadObject → `ConfirmDeliveryHandler` proof gate against live MinIO. See `docs/runbooks/object-storage.md`.

CI: integration tests are **manual / pre-release** for now — not wired into `.github/workflows/ci.yml`.

## Unit Test Structure

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';

describe('CreateOrderCommand validation', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  it('rejects empty line items', () => {
    const cmd = new CreateOrderCommand({ /* ... */ lines: [] });
    expect(() => validateAppCommand(cmd)).toThrow(AppCommandValidationError);
  });
});
```

- One behaviour per test, named with the form `<does X> when <condition>`.
- Arrange / Act / Assert blocks separated by a blank line.
- Prefer real fakes (in-memory repo, deterministic clock) over `vi.mock` of internal modules.

## Mocking

- Mock at the boundary (HTTP client, DB driver, system clock) — never mock the unit under test.
- For NestJS modules, use `Test.createTestingModule({ providers: [...] }).overrideProvider(...)` rather than module-level mocks.
- For Prisma, prefer a real Postgres in a Docker test container over `jest-mock-extended`. The repo's `docker-compose.yml` has one ready.

## Coverage Targets

- Foundation modules (`@binexus/events`, `@binexus/sdk`, command bus, tenant context, outbox): **>= 90%** branches.
- Domain modules (`orders`, `inventory`, etc.): **>= 80%** statements, with all command handlers and state transitions covered.
- UI / glue / DTO mappers: not blocked on coverage; cover via integration or E2E.

Don't game the metric. Add the test that catches the next regression.

## Snapshot Tests

Avoid except for stable, intentional outputs (e.g. JSON event envelope shapes). Never snapshot timestamps, IDs, or anything order-dependent.

## When TDD Is Required

See `tdd` skill for the workflow. Required for:

- New command handlers
- New state machine transitions
- New event producers / consumers
- Bug fixes (write the failing test first, then the fix)

Optional for: UI prototypes, throwaway scripts, type-only refactors.
