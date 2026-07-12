# Gate 7 — Nest deletion inventory

Status: **FINAL** — NestJS deleted; classification reflects completed Gate 7 ([ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)).  
Date: 2026-07-12  
Prior: Gate 6 FINAL — [`gate6-checkpoint.md`](./gate6-checkpoint.md)  
Checkpoint: [`gate7-checkpoint.md`](./gate7-checkpoint.md)

---

## Classification legend

| Class               | Meaning                                                     |
| ------------------- | ----------------------------------------------------------- |
| **DELETE**          | Removed from the repo in Gate 7                             |
| **REWRITE**         | Active docs/skills/config retargeted to .NET                |
| **KEEP_HISTORICAL** | Left in place as migration/ADR history (not a runtime path) |
| **FALSE_POSITIVE**  | Hit matched a search string but was never Nest runtime      |

---

## DELETE (completed)

| Path / item                                                             | Notes                                                            |
| ----------------------------------------------------------------------- | ---------------------------------------------------------------- |
| `apps/backend/**`                                                       | Nest monolith, Prisma, Nest tests, Fastify adapter               |
| `apps/backend` in `pnpm-workspace.yaml`                                 | Workspace entry removed                                          |
| Root scripts `*:nest`, `test:integration:nest`, `docker:up:nest-legacy` | Removed                                                          |
| Compose `redis` service + `--profile nest`                              | Removed                                                          |
| `.env.example` Nest / `REDIS_URL` / Nest JWT block                      | Removed                                                          |
| `.github/dependabot.yml` `@nestjs/*` groups                             | Removed if present                                               |
| `.github/CODEOWNERS` `/apps/backend`                                    | Removed                                                          |
| PR/issue templates Nest checklist lines                                 | Trimmed                                                          |
| Nest turbo package `@binexus/backend`                                   | Gone with tree                                                   |
| **`packages/events` (`@binexus/events`)**                               | **DELETED** — no consumers; .NET owns `backend/contracts/events` |

---

## REWRITE (completed / in docs pass)

| Hit class           | Examples                                                                                                | Action taken                                                 |
| ------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| Dual-port docs      | architecture overview, bounded-contexts, commands, events, multi-tenant, observability, CI/dev-workflow | Active text → .NET only; Nest marked historical via ADR-0015 |
| Skills Nest-default | `learn-codebase`, `webapp-testing`, `react-best-practices`                                              | Soft retarget to .NET `:5102`                                |
| Web comments / env  | `apps/web/.env.example`, `api.ts`, `playwright.config.ts`                                               | .NET-only wording                                            |
| CI filter           | `--filter=!@binexus/backend`                                                                            | Dropped (package absent)                                     |
| Domain layout paths | `docs/domains/inventory.md`, `warehouse.md`                                                             | Point at `backend/src/Modules/...`                           |
| ADR index / status  | 0007/0008 superseded; 0001/0002/0005/0006 amended; 0014+0015 indexed                                    | See `docs/adr/`                                              |

---

## KEEP_HISTORICAL

| Path                                                                                   | Note                                     |
| -------------------------------------------------------------------------------------- | ---------------------------------------- |
| `docs/migration/*-nest-audit.md`                                                       | Per-module Nest→.NET audit matrices      |
| `docs/migration/nest-dependency-audit.md`                                              | Gate 5→6 dependency audit                |
| `docs/migration/frontend-switch-*.md`, `gate6-checkpoint.md`, earlier gate checkpoints | Dual-run history                         |
| `docs/adr/0007`, `0008`                                                                | Superseded by ADR-0015; bodies unchanged |
| `docs/adr/0001`–`0006` Nest mentions in body                                           | History; status amended where needed     |
| `docs/migration/BASELINE.md`                                                           | Migration baseline                       |

Do **not** delete these. They explain why Gate 7 happened.

---

## FALSE_POSITIVE (examples)

| Hit                                                           | Why it stays                                         |
| ------------------------------------------------------------- | ---------------------------------------------------- |
| Word "nest" in unrelated English / CSS / package names        | Not NestJS                                           |
| Redis mentioned as a **rejected / future** option inside ADRs | Historical decision text                             |
| Playwright assert `no :3001`                                  | Guard against accidental Nest URL; Nest process gone |
| Prisma named in ADR-0005 body                                 | Historical; amended by ADR-0015                      |

---

## Must NOT delete (confirmed kept)

- `backend/` (.NET)
- `packages/sdk`, `packages/types`, `packages/ui`, `packages/config`
- `infra` / compose MinIO + Postgres
- EF migrations + `backend/db/binexus-idempotent.sql`

---

## Search to re-run after Gate 7

```bash
rg -n "apps/backend|@binexus/backend|@binexus/events|REDIS_URL|:3001|@nestjs|prisma" \
  --glob '!**/node_modules/**' --glob '!**/pnpm-lock.yaml' \
  --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/docs/migration/**' \
  --glob '!**/docs/adr/**'
```

Remaining hits should be REWRITE leftovers or intentional historical notes only.
