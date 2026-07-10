# Binexus Platform

Operational SaaS platform — **modular monolith**, **event-driven**, **offline-first**, **multi-tenant**, **multi-industry**.

> Mantra: **Foundation wide. Execution narrow.**

## Architecture at a glance

- **Monorepo**: pnpm + Turborepo
- **Backend**: NestJS 11 (Fastify) — bounded contexts, CQRS-lite commands, domain events, outbox pattern
- **Web**: Next.js 15 (App Router) + Tailwind 3 + React 19
- **Desktop**: Tauri 2 wrapper around the web app
- **Mobile**: placeholder — driver app planned after F5 Sales / POS stabilizes (F5.1 shipped: web `/pos`)
- **DB**: PostgreSQL 16 + Prisma
- **Cache / event transport (planned)**: Redis 7.4
- **Object storage**: MinIO (S3-compatible)
- **Auth**: JWT (access + refresh) + Argon2 + RBAC (5 roles)
- **Multi-tenant**: shared DB + `tenantId` + AsyncLocalStorage + Prisma extension
- **Observability**: Pino structured logging with request context
- **Feature flags**: `TenantFeature` table + service

## Repository layout

```
binexus-platform/
├── apps/
│   ├── web/         # Next.js 15 admin/dashboard
│   ├── backend/     # NestJS 11 API (bounded contexts)
│   ├── desktop/     # Tauri 2 wrapper
│   └── mobile/      # Placeholder (no active development)
├── packages/
│   ├── config/      # Shared tsconfig / eslint / prettier / tailwind
│   ├── types/       # Shared domain types & enums
│   ├── events/      # Domain event registry + Zod schemas
│   ├── ui/          # Minimal design system (5 components)
│   └── sdk/         # Typed API client
├── infrastructure/
│   └── compose/     # docker-compose.yml (postgres / redis / minio)
└── docs/            # Architecture, domains, states, workflows
```

## Quick start

```bash
# 1. Install deps
pnpm install

# 2. Spin up infra
pnpm docker:up

# 3. Migrate & seed DB
pnpm db:migrate
pnpm db:seed

# 4. Run everything in dev
pnpm dev
```

- Backend: <http://localhost:3001>
- Web: <http://localhost:3000>
- Postgres: `localhost:5432` (user: `binexus` / pass: `binexus` / db: `binexus`)
- Redis: `localhost:6379`
- MinIO console: <http://localhost:9001> (user: `binexus` / pass: `binexus123`) — bucket is **private**; see [`docs/runbooks/object-storage.md`](docs/runbooks/object-storage.md)

## Conventions

- TypeScript strict mode everywhere — no exceptions.
- Every database write must go through `PrismaService` (auto-injects `tenantId`).
- Cross-context communication uses **events**, never direct service calls.
- Every use case is modeled as a `Command` + `Handler`.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

## Documentation

Start with [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Roadmap

| Phase            | Scope                                                               | Status                                                                                                           |
| ---------------- | ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| F0 · Foundation  | Monorepo, auth, multi-tenant, outbox, CI, docs                      | Complete                                                                                                         |
| F1 · Orders      | Order lifecycle, approvals, warehouse handoff                       | Complete (lifecycle through delivery handoff)                                                                    |
| F2 · Inventory   | Stock, reservations, adjustments, transfers                         | Complete                                                                                                         |
| F3 · Warehouse   | Picking base (warehouse-lite)                                       | Complete (picking base)                                                                                          |
| F4 · Logistics   | Routes, dispatch, confirmation, proof, failed delivery, liquidation | Complete (happy path + MinIO hardening + failed delivery/resolution + COD liquidation + MinIO integration tests) |
| F5 · Sales / POS | Retail sessions (per terminal), tickets, cash sales (5.1 done)      | Active — **5.1 done**; next: 5.2 split payment                                                                   |
| F7 · Billing     | Invoices, receivables, payment allocation                           | Planned                                                                                                          |
| F8 · Reporting   | Dashboards and analytics projections                                | Planned                                                                                                          |

See [`CHANGELOG.md`](CHANGELOG.md) for merged PR history. Domain detail lives in [`docs/domains/`](docs/domains/).
