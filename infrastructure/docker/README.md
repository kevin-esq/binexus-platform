# Application Dockerfiles (Gate 6 FINAL)

| File                 | Image                                                                     |
| -------------------- | ------------------------------------------------------------------------- |
| `Dockerfile.api`     | `Binexus.Api` (+ `efbundle` for dedicated migrate service)                |
| `Dockerfile.workers` | `Binexus.Workers` + Kestrel `/health` on `:8081`                          |
| `Dockerfile.web`     | Optional Next.js (`compose --profile web`); `NEXT_PUBLIC_*` via build-arg |
| `entrypoint-api.sh`  | Optional `RUN_MIGRATIONS=1` (default **0**); then Api                     |

Build from **repo root**:

```bash
docker build -f infrastructure/docker/Dockerfile.api -t binexus-api .
docker build -f infrastructure/docker/Dockerfile.workers -t binexus-workers .
```

**Production:** run compose/`efbundle` **migrate once** → deploy Api and Workers with `RUN_MIGRATIONS=0`. Do not migrate on every API replica.

Compose: `../compose/docker-compose.yml` and [`docs/migration/gate6-checkpoint.md`](../../docs/migration/gate6-checkpoint.md).
