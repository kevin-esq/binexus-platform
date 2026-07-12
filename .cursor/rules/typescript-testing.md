---
description: 'TypeScript/JavaScript testing extending common rules'
globs: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx']
alwaysApply: false
---

# TypeScript/JavaScript Testing

Extends `common-testing.md` with stack-specific guidance for this monorepo.

## Frameworks

- **Vitest** — unit tests in `packages/*` and `apps/web` where configured. Script: `vitest run --passWithNoTests`.
- **Playwright** — E2E in `apps/web/e2e/` (gate smoke against .NET Api `:5102`).
- **.NET tests** — Architecture / Unit / Integration under `apps/backend/tests/` (`pnpm test:dotnet`, `pnpm test:integration`).

## Where Tests Live

- Packages: co-located `*.spec.ts` / `*.test.ts`.
- Web E2E: `apps/web/e2e/`.
- Backend: `apps/backend/tests/Binexus.{Architecture,Unit,Integration}Tests`.

## Commands

| Command                 | Scope                             |
| ----------------------- | --------------------------------- |
| `pnpm test`             | Turbo Vitest across Node packages |
| `pnpm test:dotnet`      | Full .NET solution Release        |
| `pnpm test:integration` | .NET IntegrationTests             |
| `pnpm docker:smoke`     | Compose stack + `SMOKE_REQUIRE=1` |

Backend integration uses PostgreSQL (Testcontainers / fixture) and MinIO when required — not Redis.
