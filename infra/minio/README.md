# MinIO bucket + CORS for local proof uploads

Used when `Logistics:Storage:Provider=MinIO` (Production-like local, or Gate 5 optional MinIO path).

## Files

| File              | Purpose                                                   |
| ----------------- | --------------------------------------------------------- |
| `cors.json`       | Bucket CORS allowing `http://localhost:3000` PUT/GET/HEAD |
| `init-bucket.sh`  | Creates bucket + applies CORS via `mc`                    |
| `init-bucket.ps1` | Same for Windows PowerShell                               |

## Apply against a running MinIO

```powershell
# Example: MinIO on :9000, console :9001, creds binexus / binexus12345
pwsh -File infra/minio/init-bucket.ps1 `
  -Endpoint http://127.0.0.1:9000 `
  -AccessKey binexus `
  -SecretKey binexus12345 `
  -Bucket binexus-proofs
```

Browser origin for the operator panel is `http://localhost:3000`. Without CORS, proof `PUT` to the presigned URL fails in the browser (API integration tests still pass because they use `HttpClient`).

## Gate 5 stack

Default Gate 5 smoke uses `Logistics:Storage:Provider=Local` (API `/internal/dev-object-storage`). MinIO is optional; integration coverage lives in `MinioProofStorageTests` (Testcontainers MinIO).
