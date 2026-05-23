# Binexus Platform

Operational SaaS platform — **modular monolith**, **event-driven**, **offline-first**, **multi-tenant**, **multi-industry**.

> Mantra: **Foundation wide. Execution narrow.**

## Architecture at a glance

- **Monorepo**: pnpm + Turborepo
- **Backend**: NestJS 11 (Fastify) — bounded contexts, CQRS-lite commands, domain events, outbox pattern
- **Web**: Next.js 15 (App Router) + Tailwind 3 + React 19
- **Desktop**: Tauri 2 wrapper around the web app
- **Mobile**: not under active development in Phase 0
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
- MinIO console: <http://localhost:9001> (user: `binexus` / pass: `binexus123`)

## Conventions

- TypeScript strict mode everywhere — no exceptions.
- Every database write must go through `PrismaService` (auto-injects `tenantId`).
- Cross-context communication uses **events**, never direct service calls.
- Every use case is modeled as a `Command` + `Handler`.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/).

## Documentation

Start with [`docs/architecture/overview.md`](docs/architecture/overview.md).

## Roadmap

| Phase | Scope                                  | Status      |
| ----- | -------------------------------------- | ----------- |
| 0     | Foundation (this)                      | In progress |
| 1     | Orders (the core operational workflow) | Next        |
| 2     | Inventory                              | Planned     |
| 3     | Sales / POS retail                     | Planned     |
| 4     | Warehouse (lite, not WMS enterprise)   | Planned     |
| 5     | Routes                                 | Planned     |
| 6     | Liquidation                            | Planned     |
| 7     | Billing                                | Planned     |
| 8     | Analytics & dashboards                 | Planned     |
