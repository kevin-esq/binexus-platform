# Binexus .NET backend

Modular monolith (.NET 10) at `apps/backend/`. See:

- [`docs/architecture/dotnet-backend.md`](../../docs/architecture/dotnet-backend.md)
- [`docs/migration/`](../../docs/migration/) (Gate checkpoints; historical Nest notes remain)

## Quick start

From repo root:

```bash
dotnet restore apps/backend/Binexus.slnx
dotnet build apps/backend/Binexus.slnx -c Release
dotnet test apps/backend/Binexus.slnx -c Release
pnpm dev:api
```

Health: `GET http://localhost:5102/health` / `.../health/ready` / `.../health/runtime`.

Runtime mode is required via `Binexus:RuntimeMode` (`Cloud` or `Branch`). See [`docs/migration/pr1-runtime-mode-foundation-checkpoint.md`](../../docs/migration/pr1-runtime-mode-foundation-checkpoint.md).
