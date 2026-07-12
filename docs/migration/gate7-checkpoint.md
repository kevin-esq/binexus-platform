# CHECKPOINT GATE 7 — FINAL

**Status:** CLOSED (verification complete in this workspace; user controls Git — no commit/push by agent)  
**Date:** 2026-07-12  
**ADR:** [0015 — NestJS retirement / .NET sole backend](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)  
**Note:** User brief said “Superseded by ADR-0014”; ADR-0014 already documents inventory sync. Nest retirement is **ADR-0015**.

Prior ops (Gate 6 conditions, applied before Nest delete):

1. Compose smoke uses isolated `COMPOSE_PROJECT_NAME` + `API_SMOKE_PORT`…; **never** kills host processes; busy port → abort with PID guidance. Test: `backend/scripts/gate6-smoke-no-kill-host.test.ps1` → **PASS**.
2. Known JWT example rejected in Staging/Production; allowed in Development/Testing. `.env.example` marked DEVELOPMENT ONLY.

---

## Inventario eliminado

| Item                                                                 | Action                                                  |
| -------------------------------------------------------------------- | ------------------------------------------------------- |
| `apps/backend/**`                                                    | DELETE                                                  |
| Compose `redis` + `--profile nest` + `redis_data`                    | DELETE                                                  |
| Root `*:nest`, `docker:up:nest-legacy`, `--filter=!@binexus/backend` | DELETE / REWRITE                                        |
| Nest block in `.env.example` / `REDIS_URL`                           | DELETE                                                  |
| `@binexus/events` (`packages/events`)                                | DELETE (no consumers; SoT = `backend/contracts/events`) |
| Dependabot nest/prisma groups; CODEOWNERS Nest paths                 | DELETE                                                  |
| CI Nest filters / nest profile teardown                              | REWRITE                                                 |

Historical nest audits / Gate 5–6 docs: **KEEP_HISTORICAL**.

---

## Archivos/directorios eliminados

- `apps/backend/` (Nest, Fastify, Prisma schema/migrations/seeds, Vitest Nest, Redis streams stub, outbox Nest)
- `packages/events/`
- Compose redis service + volume

---

## Paquetes Node eliminados

Workspace no longer resolves: `@binexus/backend`, `@nestjs/*`, `prisma` / `@prisma/*`, Nest-only Fastify/pino/argon2 (Node), Redis client (was Nest-only).  
Lockfile refreshed via `pnpm install` (not hand-edited). `pnpm install --frozen-lockfile` → **OK**.

Kept: Vitest where used by packages; `@binexus/sdk`, `@binexus/types`, `@binexus/ui`, `@binexus/config`.

---

## Packages compartidos

| Package           | Decision | Reason                                            |
| ----------------- | -------- | ------------------------------------------------- |
| `packages/sdk`    | KEEP     | OpenAPI client; web consumer                      |
| `packages/types`  | KEEP     | UI + SDK DTOs still imported                      |
| `packages/events` | DELETE   | Nest-only; `.NET` owns `backend/contracts/events` |
| `packages/ui`     | KEEP     | Web transpile dependency                          |
| `packages/config` | KEEP     | ESLint/TS shared; Nest prisma ignore removed      |

---

## Scripts finales (root)

`pnpm dev`, `dev:web`, `dev:backend` / `dev:api`, `dev:workers`, `build`, `test`, `test:integration`, `test:dotnet`, `docker:up|down|smoke|smoke:win`, `db:migrate`, `db:seed:dev` (+ `:win`).  
No `*:nest`, no `--filter=!@binexus/backend`.

---

## Servicios Compose finales

`docker compose … config --services` (default profiles):

```text
postgres
migrate
minio
minio-bucket-init
api
workers
```

Optional profiles: `web`, `seed`. **No redis. No Nest.**

---

## CI final

Jobs required: `frontend` | `backend` | `compose-smoke` → `ci-summary`.  
No Nest filters; compose smoke uses `COMPOSE_PROJECT_NAME=binexus-ci-smoke` + standard ports for Playwright; teardown `-p binexus-ci-smoke` without nest profile.

---

## Búsqueda referencias legacy

Allowed residuals: `docs/migration/*-nest-audit.md`, superseded ADRs 0007/0008, Gate 5–6 history, e2e assert “no `:3001`”, skills soft-updated.  
No executable Nest package, no Redis compose, no `REDIS_URL` in `.env.example`, no root nest scripts.

---

## Documentación / ADRs

- README + architecture + events + local-setup: .NET-only active wording
- ADR-0015 Accepted; 0007/0008 **Superseded by ADR-0015**; 0001/0002/0005/0006 Amended
- Notion Home + Roadmap updated (Gate 7 closed)

---

## Base limpia

**No hay migración de datos cuid/Prisma → UUIDv7/EF.** Recrear:

```bash
docker compose -f infrastructure/compose/docker-compose.yml --profile web --profile seed down -v --remove-orphans
pnpm docker:up
pnpm db:seed:dev   # o db:seed:dev:win
```

Rollback del PR = **Git**, no Nest contra schema EF.

---

## Artefactos / verificación

| Check                                     | Result                                          |
| ----------------------------------------- | ----------------------------------------------- |
| `pnpm install --frozen-lockfile`          | PASS                                            |
| `dotnet restore/build Release`            | PASS (0 warnings)                               |
| `dotnet test` Arch+Unit+Integration       | **237** passed (29+58+150), 0 failed, 0 skipped |
| JWT Production/Staging reject example key | PASS (new tests)                                |
| `has-pending-model-changes`               | No pending                                      |
| NuGet vulnerable High/Critical            | **0**                                           |
| SDK test/typecheck                        | 9/9 + PASS                                      |
| Web typecheck / lint / build              | PASS                                            |
| No-kill host smoke policy test            | PASS                                            |
| `pnpm docker:smoke:win` (isolated ports)  | **GATE6 COMPOSE SMOKE PASS**                    |
| Nest/Redis in default stack               | **0**                                           |

---

## Resumen del diff (working tree, approx.)

| Metric                             | Approx.                                          |
| ---------------------------------- | ------------------------------------------------ |
| Tracked files touched (shortstat)  | 294 changed, +1425 / −22271                      |
| Deleted paths (Nest tree dominant) | ~230                                             |
| Packages removed                   | Nest stack + `@binexus/events`                   |
| Packages added                     | none required for Gate 7                         |
| EF migrations                      | unchanged this gate                              |
| Tests                              | 237 .NET + 9 SDK + smoke + Playwright path in CI |

---

## Riesgos residuales

- Local `.env` may still contain old Nest keys — harmless if unused; prune manually.
- Skills/rules may still mention Nest in historical examples — non-executable.
- Full CI (GH Actions) not re-run from this agent session; local gates green.
- `apps/web/test-results/` may appear untracked — do not commit.
