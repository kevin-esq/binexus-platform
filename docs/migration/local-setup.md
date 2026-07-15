# Local setup (.NET only)

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL. NestJS is not supported. Legacy backend: NestJS, removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md) migration.

## First boot

```bash
pnpm install
cp .env.example .env   # Jwt__SigningKey is DEVELOPMENT ONLY
pnpm docker:up         # postgres, minio, migrate, api, workers
pnpm db:seed:dev       # or db:seed:dev:win on Windows
pnpm dev:web           # http://localhost:3000 → Api :5102
```

| Surface         | URL                                   |
| --------------- | ------------------------------------- |
| Api liveness    | http://localhost:5102/health          |
| Api readiness   | http://localhost:5102/health/ready    |
| Api runtime     | http://localhost:5102/health/runtime  |
| Workers         | http://localhost:5103/health          |
| Workers runtime | http://localhost:5103/health/runtime  |
| Web             | http://localhost:3000                 |
| MinIO           | http://localhost:9000 / console :9001 |

`Binexus__RuntimeMode` is required (no image/code default). Local Compose, `.env.example`, and launch profiles set `Cloud` explicitly for PR 1 compatibility. Use `Branch` only when you intend the Branch composition root.

## Clean database recreate

There is **no** data migration from cuid/Prisma to UUIDv7/EF. After Nest retirement (or any dual-schema experiment), recreate the local database:

```bash
docker compose -f infrastructure/compose/docker-compose.yml --profile web --profile seed down -v --remove-orphans
pnpm docker:up
pnpm db:seed:dev   # or: pnpm db:seed:dev:win
```

- `down -v` drops named volumes (Postgres data + MinIO data for this compose project).
- `pnpm docker:up` runs the dedicated `migrate` service once, then starts Api/Workers with `RUN_MIGRATIONS=0`.
- Rollback for Gate 7 is **Git**, not re-running Nest against the EF schema.

## Useful commands

```bash
pnpm db:migrate
pnpm db:migrate:script    # regenerates apps/backend/db/binexus-idempotent.sql
pnpm docker:smoke         # Linux; Windows: docker:smoke:win
pnpm test:dotnet
pnpm test:integration
```

See also root [`README.md`](../../README.md) and [`gate7-checkpoint.md`](./gate7-checkpoint.md).
