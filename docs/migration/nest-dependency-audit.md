# Nest dependency audit (Gate 5 → Gate 6)

> **KEEP_HISTORICAL** — Nest deleted in Gate 7 ([ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)). See [`gate7-deletion-inventory.md`](./gate7-deletion-inventory.md).

Date: 2026-07-12  
Scope: runtime + build + docs references to Nest (`apps/backend`, `:3001`, `@nestjs/*`).  
Rule: **do not delete Nest** until Gate 7. Gate 6 wired Docker/Compose/CI for .NET.

## Summary

| Surface                    | Runtime Nest dependency                            | Classification             |
| -------------------------- | -------------------------------------------------- | -------------------------- |
| `packages/sdk`             | None (no hardcoded host)                           | OK                         |
| `apps/web` operator client | Default `http://localhost:5102`                    | OK                         |
| Root `.env.example`        | .NET first; Nest block marked `REMOVE_IN_GATE_7`   | STILL_REQUIRED_TEMPORARILY |
| Compose default            | No Nest service; Redis only under `--profile nest` | OK (.NET path)             |
| GHA Nest turbo + Redis     | Temporary until Gate 7                             | STILL_REQUIRED_TEMPORARILY |
| GHA `dotnet-smoke`         | Compose + SMOKE_REQUIRE=1                          | OK (Gate 6)                |

## Classified hits

### REMOVE_IN_GATE_7 (was REMOVE_IN_GATE_6 for Nest tree)

| Path                                            | Hit            | Notes                        |
| ----------------------------------------------- | -------------- | ---------------------------- |
| `apps/backend/**`                               | Nest codebase  | Delete only with Gate 7 auth |
| Root `.env.example` Nest / Redis block          | Dual-run       | Drop with Nest               |
| Compose `redis` profile + GHA Redis service     | Nest only      | Drop with Nest               |
| `.github/dependabot.yml` `@nestjs/*`            | Nest updates   | Drop with Nest               |
| `.github/CODEOWNERS` `/apps/backend/...`        | Nest ownership | Drop with Nest               |
| Nest turbo jobs / `package.json` Prisma scripts | Nest CI        | Drop with Nest               |

### DONE_IN_GATE_6

| Path                                        | Change                                                     |
| ------------------------------------------- | ---------------------------------------------------------- |
| Root `.env.example`                         | .NET Jwt/DB/storage defaults; web → `:5102`                |
| `infrastructure/compose/docker-compose.yml` | api + workers + postgres + minio; Redis profile `nest`     |
| `infrastructure/docker/Dockerfile.*`        | Api / Workers / optional web                               |
| `.github/workflows/ci.yml`                  | `dotnet-smoke` job                                         |
| Root `package.json`                         | `docker:*`, `dev:web`, `db:migrate:dotnet`, `smoke:dotnet` |

### HISTORICAL_DOC

| Path                           | Hit                 |
| ------------------------------ | ------------------- |
| `docs/adr/*` Nest mentions     | Historical          |
| `docs/migration/*` checkpoints | Nest vs .NET tables |

## Gate 7 follow-ups

See checklist in [`gate6-checkpoint.md`](./gate6-checkpoint.md) — readiness for Gate 7.
