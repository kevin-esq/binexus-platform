# CHECKPOINT GATE 6 — FINAL

Date: 2026-07-12  
Status: **CLOSED** — Nest remaining until Gate 7; Nest deleted in Gate 7 ([`gate7-checkpoint.md`](./gate7-checkpoint.md), [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)).  
Prior: Gate 5 FINAL — [`frontend-switch-checkpoint.md`](./frontend-switch-checkpoint.md)  
Gate 7 inventory: [`gate7-deletion-inventory.md`](./gate7-deletion-inventory.md)

---

## Verdict

Compose defaults to **.NET only** (postgres, minio, dedicated `migrate`, api, workers). Nest Redis is `--profile nest` / `REMOVE_IN_GATE_7`. Required CI is frontend (sdk+web) + .NET backend + compose-smoke (MinIO + Playwright). Nest turbo/prisma/Redis GHA paths removed from required jobs.

---

## How to run

```bash
cp .env.example .env   # Jwt__SigningKey from example (32+ chars)
pnpm docker:up         # migrate once → api + workers (RUN_MIGRATIONS=0)
pnpm dev:web           # Next → http://localhost:5102
pnpm docker:smoke      # Linux/GHA; Windows: pnpm docker:smoke:win
```

| Surface        | URL                                                                    |
| -------------- | ---------------------------------------------------------------------- |
| Api liveness   | http://localhost:5102/health or `/health/live`                         |
| Api readiness  | http://localhost:5102/health/ready (PG + EF migrations; **not** MinIO) |
| Workers health | http://localhost:5103/health                                           |
| MinIO          | http://localhost:9000 / console :9001                                  |

### Production deploy order

1. Run **migrate job** once (`efbundle` / `dotnet ef database update` / idempotent SQL).
2. Deploy Api + Workers with `RUN_MIGRATIONS=0`.
3. Never migrate on every API replica.

Idempotent SQL: `backend/db/binexus-idempotent.sql` (tracked; also CI upload). Regenerate: `pnpm db:migrate:script`.

---

## Requirements checklist

| #   | Requirement                                                                                                                            | Result    |
| --- | -------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| 1   | Dedicated `migrate` service (efbundle), exits 0; API/Workers `RUN_MIGRATIONS=0`; depends_on migrate                                    | Done      |
| 2   | Compose .NET default; pinned MinIO + mc; workers depend migrate not api; smoke waits api+workers; nest profile marked REMOVE_IN_GATE_7 | Done      |
| 3   | `InternalEndpoint` / `PublicEndpoint` for MinIO; compose smoke MinIO presign→PUT                                                       | Done      |
| 4   | `/health` + `/health/live`; `/health/ready` = PG + migrations; MinIO not required for ready; Workers Kestrel `/health`                 | Done      |
| 5   | `pnpm db:migrate`, `db:seed:dev`, `db:seed:testing`; compose migrate-only; optional `--profile seed`                                   | Done      |
| 6   | CI required: frontend, backend, compose-smoke; Nest off required path; no continue-on-error; logs on smoke failure                     | Done      |
| 7   | Root scripts cross-platform; `docker:smoke` bash primary                                                                               | Done      |
| 8   | Dockerfiles: pin MinIO, non-root, multi-stage, frozen-lockfile web, NEXT_PUBLIC build-arg                                              | Done      |
| 9   | Smoke sanitizes presigned URLs; `.env.example` local-only + secret-store note                                                          | Done      |
| 10  | Gate 7 inventory doc                                                                                                                   | Done      |
| 11  | Verification commands                                                                                                                  | See below |
| 12  | This FINAL checkpoint                                                                                                                  | Done      |

---

## Compose services (default)

| Service             | Role                                                                       |
| ------------------- | -------------------------------------------------------------------------- |
| `postgres`          | PG 16                                                                      |
| `minio`             | `minio/minio:RELEASE.2025-04-22T22-12-26Z` + `MINIO_API_CORS_ALLOW_ORIGIN` |
| `minio-bucket-init` | `minio/mc:RELEASE.2025-04-16T18-13-26Z` — create `binexus-dev`             |
| `migrate`           | One-shot efbundle; idempotent re-run                                       |
| `api`               | `:5102`, migrate completed, `RUN_MIGRATIONS=0`                             |
| `workers`           | `:5103` health; migrate completed; **no** depends_on api                   |
| `web`               | profile `web`                                                              |
| `seed`              | profile `seed` — `dotnet Binexus.Api.dll --seed`                           |
| `redis`             | profile `nest` — **REMOVE_IN_GATE_7**                                      |

---

## MinIO endpoints

| Config             | Compose value           | Use                                  |
| ------------------ | ----------------------- | ------------------------------------ |
| `InternalEndpoint` | `http://minio:9000`     | S3 Head/Exists from Api container    |
| `PublicEndpoint`   | `http://localhost:9000` | Presigned PUT for browser/host smoke |
| `Provider`         | `MinIO`                 | Default compose / smoke              |

---

## CI (required)

| Job             | Contents                                                                               |
| --------------- | -------------------------------------------------------------------------------------- |
| `frontend`      | turbo typecheck/lint/test/build `--filter=!@binexus/backend`                           |
| `backend`       | .NET format, build, has-pending-model-changes, test, SDK diff, idempotent SQL artifact |
| `compose-smoke` | `SMOKE_REQUIRE=1` MinIO + Playwright `gate5-smoke.spec.ts`; upload logs on failure     |

**Deleted from required path (document for reviewers):**

- Nest `prisma generate` on install/typecheck/lint/build/test
- Nest turbo without filter
- GHA Redis service + Nest `test:integration`
- Job name `dotnet-smoke` → renamed `compose-smoke`

No `continue-on-error: true` on required gates.

---

## Nest remaining (until Gate 7)

**Deleted in Gate 7.** See [`gate7-checkpoint.md`](./gate7-checkpoint.md) and [`gate7-deletion-inventory.md`](./gate7-deletion-inventory.md).

---

## Image sizes (local verify 2026-07-12)

| Image                                      | Size      |
| ------------------------------------------ | --------- |
| `binexus-api:local`                        | **493MB** |
| `binexus-workers:local`                    | **384MB** |
| `minio/minio:RELEASE.2025-04-22T22-12-26Z` | **250MB** |
| `minio/mc:RELEASE.2025-04-16T18-13-26Z`    | **116MB** |

---

## Verification

```bash
docker compose -f infrastructure/compose/docker-compose.yml --env-file .env down -v --remove-orphans
# .env from .env.example
docker compose -f infrastructure/compose/docker-compose.yml --env-file .env build
docker compose -f infrastructure/compose/docker-compose.yml --env-file .env up -d
# wait healthy; prove no redis service in default profile
pnpm docker:smoke          # SMOKE_REQUIRE=1 MinIO (or docker:smoke:win)
dotnet test -c Release     # from backend/
pnpm --filter @binexus/sdk test
pnpm --filter @binexus/web typecheck && lint && build
dotnet ef migrations has-pending-model-changes ...
```

### Results (2026-07-12)

| Check                                                   | Result                                            |
| ------------------------------------------------------- | ------------------------------------------------- |
| Empty DB migrate + idempotent re-run                    | **PASS**                                          |
| Compose smoke MinIO presign→PUT (`host=localhost:9000`) | **PASS**                                          |
| No redis in default `compose ps --services`             | **PASS**                                          |
| `dotnet test -c Release`                                | **233/233** (29 arch + 58 unit + 146 integration) |
| `has-pending-model-changes`                             | **PASS** (no pending)                             |
| SDK test                                                | **9/9**                                           |
| Web typecheck / lint / build                            | **PASS**                                          |
| Idempotent SQL                                          | `backend/db/binexus-idempotent.sql` (~64KB)       |

**Note:** Smoke scripts stop a host `Binexus.Api`/`dotnet` process on `:5102` if present — it steals traffic from compose and makes MinIO look like Local (`:5102`).

---

## Secrets / logs

- `.env.example` states values are local defaults; prod uses a secret store.
- Smoke logs upload URL **host** only, never full presigned query.
- Jwt from `.env.example`: `local-build-signing-key-with-more-than-thirty-two-bytes`.

---

## Risks

1. Dual Nest/.NET until Gate 7 — wrong `NEXT_PUBLIC_API_URL` can still hit `:3001`.
2. Host `dotnet run` on `:5102` conflicts with compose (smoke scripts mitigate).
3. UUID clean DB only — no Nest cuid migration.
4. Playwright in CI starts Next via `webServer` while compose Api stays up (`KEEP_RUNNING=1`).
5. Options binding for Logistics storage uses `set` accessors (env `Logistics__Storage__*` must bind for MinIO).
