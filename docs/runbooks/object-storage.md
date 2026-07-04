# Object storage (MinIO / S3)

Binexus uses MinIO locally (`S3_BUCKET=binexus-dev`) and S3-compatible storage in production. Delivery proof photos and signatures are uploaded via **presigned PUT** URLs issued by the backend.

## Private bucket policy

The `binexus-dev` bucket is **not** public. Anonymous read (`mc anonymous set download`) was removed on purpose: proof media must not be world-readable if someone guesses an object key.

To serve proof files to authorized users in the future, issue **presigned GET** URLs with a short TTL from the backend — never re-enable anonymous download on the proof bucket.

## CORS for browser uploads (local dev)

MinIO **Community Edition** does not support bucket-level `mc cors set` (that API is MinIO AIStor / paid). Browser PUT from `http://localhost:3000` uses **cluster-wide** CORS instead:

```yaml
# infrastructure/compose/docker-compose.yml → minio service
MINIO_API_CORS_ALLOW_ORIGIN: 'http://localhost:3000'
```

After changing this env var, recreate the MinIO container: `docker compose -f infrastructure/compose/docker-compose.yml up -d minio`.

## Reset a dev bucket that still allows anonymous download

Docker Compose init only sets policy on **new** installs. If your volume predates the private-bucket change, the old public-read policy may still be active.

Run once (MinIO must be up). Use `--entrypoint` so the init service’s default script (alias + mb) does **not** run again:

```bash
docker compose -f infrastructure/compose/docker-compose.yml run --rm \
  --entrypoint /bin/sh minio-bucket-init \
  -c "mc alias set local http://minio:9000 binexus binexus123 && mc anonymous set none local/binexus-dev"
```

PowerShell:

```powershell
docker compose -f infrastructure/compose/docker-compose.yml run --rm --entrypoint /bin/sh minio-bucket-init -c "mc alias set local http://minio:9000 binexus binexus123 && mc anonymous set none local/binexus-dev"
```

Expected output: `Added local successfully` and `Access permission for binexus-dev is set to private` (or similar). No CORS error.

Alternative (destructive): `docker compose -f infrastructure/compose/docker-compose.yml down -v` then `pnpm docker:up` — wipes Postgres, Redis, and MinIO data.

## Confirm delivery and object existence

When `confirm-delivery` includes `photoObjectKey` or `signatureObjectKey`, the backend runs **HeadObject** against MinIO before persisting proof metadata. Missing objects return **400**; unreachable MinIO returns **503** within ~5s (connection timeout 3s, request timeout 5s).

Confirming **without** proof object keys is unchanged — no HeadObject call.

## Environment variables

See root `.env.example`: `S3_ENDPOINT`, `S3_REGION`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET`, `S3_PRESIGNED_UPLOAD_TTL_SECONDS`, `S3_REQUEST_TIMEOUT_MS`.
