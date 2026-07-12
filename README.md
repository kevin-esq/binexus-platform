# Binexus Platform

Operational SaaS platform — **modular monolith**, **event-driven**, **offline-first**, **multi-tenant**.

> Mantra: **Foundation wide. Execution narrow.**

## Architecture at a glance

- **Monorepo**: pnpm + Turborepo (web + shared packages) + .NET solution
- **Backend**: C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL (`apps/backend/` — Api, Workers, Platform, Modules)
- **Web**: Next.js (App Router) + Tailwind + React — operator panel at `:3000` → Api `:5102`
- **DB**: PostgreSQL 16 + EF Core migrations (no Prisma)
- **Object storage**: MinIO (S3-compatible) for delivery proofs
- **Auth**: JWT (access + refresh) + RBAC
- **Legacy**: NestJS backend removed in Gate 7 (ADR-0015). Historical notes remain under `docs/migration/` and superseded ADRs.

## Repository layout

```
binexus-platform/
├── apps/
│   ├── web/         # Next.js operator panel
│   ├── desktop/     # Tauri wrapper
│   └── mobile/      # Placeholder
├── apps/backend/    # .NET Api / Workers / Modules
├── packages/        # types, sdk, ui, config
├── infrastructure/
│   ├── compose/     # postgres / minio / migrate / api / workers (+ optional web/seed)
│   └── docker/      # Dockerfiles
└── docs/
```

## Quick start (.NET)

```bash
# 1. Install deps
pnpm install

# 2. Env — Jwt__SigningKey in .env.example is DEVELOPMENT ONLY (never Staging/Production)
cp .env.example .env

# 3. Compose: postgres, minio, migrate (once), api, workers
pnpm docker:up

# 4. Web on the host
pnpm dev:web
```

| Surface             | URL                                |
| ------------------- | ---------------------------------- |
| Api liveness        | http://localhost:5102/health       |
| Api readiness       | http://localhost:5102/health/ready |
| Workers health      | http://localhost:5103/health       |
| Web                 | http://localhost:3000              |
| MinIO API / console | http://localhost:9000 / :9001      |

Demo login (after Dev seed): tenant `acme`, `admin@acme.test`, password from `IdentitySeed__AdminPassword`.

### Clean local database (required after Nest → .NET)

There is **no** data migration from cuid/Prisma to UUIDv7/EF. Recreate the database:

```bash
docker compose -f infrastructure/compose/docker-compose.yml --profile web --profile seed down -v --remove-orphans
pnpm docker:up
pnpm db:seed:dev   # or db:seed:dev:win on Windows
```

Rollback for this migration is **Git**, not re-running Nest against the new schema.

### Useful scripts

```bash
pnpm docker:up / docker:down / docker:smoke   # Linux smoke; docker:smoke:win for PowerShell
pnpm db:migrate                               # EF Core
pnpm db:seed:dev
pnpm dev / dev:web / dev:backend / dev:workers
pnpm test / test:backend / test:integration
```

Compose smoke uses isolated ports by default (`API_SMOKE_PORT=5112`, …) and **never** kills host processes. Override ports via env if needed.

## Conventions

- TypeScript strict mode for web/packages.
- Cross-context communication uses **events** (schemas under `apps/backend/contracts/events`), never direct module→module domain calls.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

## Documentation

Start with [`docs/architecture/overview.md`](docs/architecture/overview.md).  
Migration: [`docs/migration/`](docs/migration/) (Gate 5–7 checkpoints). ADRs: [`docs/adr/`](docs/adr/).

## Roadmap

| Phase            | Scope                                          | Status   |
| ---------------- | ---------------------------------------------- | -------- |
| F0 · Foundation  | Monorepo, auth, multi-tenant, outbox, CI, docs | Complete |
| F1–F4            | Orders, Inventory, Warehouse, Logistics        | Complete |
| F5 · Sales / POS | Retail sessions, tickets, cash sales           | Complete |
| Gate 5–6         | Frontend switch + Docker / Compose / CI        | Complete |
| Gate 7           | NestJS removed; .NET sole backend              | Complete |
| F7 · Billing     | Invoices, receivables                          | Planned  |
| F8 · Reporting   | Dashboards                                     | Planned  |

See [`CHANGELOG.md`](CHANGELOG.md) for merged PR history.
