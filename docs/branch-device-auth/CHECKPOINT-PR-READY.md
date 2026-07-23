# CHECKPOINT — BRANCH DEVICE AUTHENTICATION PR READY

**Status:** Gates 1–10 closed for this initiative. Ready for human review of commit split; **no commits / push / PR executed**.
**Branch:** `feat/branch-device-auth`
**Base:** `origin/main` @ `2c95236`
**Parent:** [branch-operational-security.md](../architecture/branch-operational-security.md)
**PLAN:** [PLAN.md](./PLAN.md)
**Prior:** [CHECKPOINT-VALIDATION.md](./CHECKPOINT-VALIDATION.md) (accepted intermediate)

---

## 1. Branch and base

| Item         | Value                                       |
| ------------ | ------------------------------------------- |
| Branch       | `feat/branch-device-auth`                   |
| Base         | `origin/main` (`2c95236`)                   |
| Merge-base   | `2c9523686d8c3d31722c88988ef1a3d3d1b69d36`  |
| Working tree | dirty (implementation + gates; uncommitted) |

---

## 2. `git status --short`

~89 short-status paths (modified + untracked). Headline areas:

- `apps/backend/.../Branching/DeviceAuth/**` (new)
- `BranchDeviceAuthOptions*` + migration `20260718093009_Platform_BranchDeviceAuth`
- `Binexus.Api` Program rate limit + OpenAPI transformer + `appsettings.Testing.json`
- Module endpoints: Sales / Inventory / Orders / Warehouse / Logistics → `RequireOperationalAuthorization`
- Desktop `device_auth` session + `device_auth_interop` binary
- Tests: HttpMatrix, RateLimit, RustProductInterop, SecurityMatrix, MigrationBackfill, OpenAPI AND
- `artifacts/openapi/binexus-branch-v1.json` regenerated
- `artifacts/openapi/binexus-v1.json` restored to HEAD (Cloud, no DeviceAuth)
- Docs under `docs/branch-device-auth/` + architecture notes

Spikes / `apps/desktop/spikes/target/**` remain local noise; exclude from PR commits.

---

## 3. Diff classified

| Area                | What landed                                                                                             |
| ------------------- | ------------------------------------------------------------------------------------------------------- |
| Platform DeviceAuth | challenges, atomic consume, DAT HS256+kid, stamp, 15s cache, fail-closed, Problem Details               |
| Admin               | revoke, disable terminal, rebind (rename-in-place + stamp bump + cache evict)                           |
| Rate limit          | GlobalLimiter chained: global + IP + DeviceId; invalid IDs → shared bucket                              |
| AuthZ               | `BranchDeviceAndUser` + tenant/branch claim coherence (`USER_BRANCH_MISMATCH`)                          |
| OpenAPI             | `branch-v1` device-auth; Branch default `/openapi/v1.json` stamps Dev+User **AND** on operational paths |
| Desktop             | RAM-only DAT, single-flight, public IPC states, interop harness                                         |
| Migration           | `security_stamp` backfill + `device_auth_challenges` indexes; Down drops table/column                   |
| Tests               | HTTP matrix, rate limit, Rust→Kestrel→PG, cache/503 Sales, OpenAPI AND, lab-key validator               |

---

## 4. Security matrix (GATE 1)

| scenario                                     | test name                                                                                  | layer                   | expected HTTP   | expected public error code                      | result |
| -------------------------------------------- | ------------------------------------------------------------------------------------------ | ----------------------- | --------------- | ----------------------------------------------- | ------ |
| wrong private key                            | `Tokens_rejects_signature_from_a_different_private_key_without_leaking_device_state`       | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| wrong DeviceId                               | `Tokens_rejects_mismatched_device_id_with_generic_proof_invalid`                           | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| wrong BranchInstanceId                       | `Me_rejects_dat_stamped_for_a_different_branch_instance`                                   | integration             | 401/403         | branch mismatch / token invalid path            | pass   |
| firma malformada / protocol desconocido      | `Tokens_rejects_malformed_signature_or_unknown_protocol_version`                           | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| challenge expirado                           | `Tokens_reports_expired_challenge`                                                         | integration             | 401             | `DEVICE_CHALLENGE_EXPIRED`                      | pass   |
| Device PendingConfirmation (challenge/token) | `Public_device_auth_failures_are_indistinguishable` / `Tokens_reject_non_active_devices_*` | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| Device Revoked al challenge                  | same anti-enum + Active-only mint                                                          | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| Device Revoked con DAT vigente               | `Issue_me_revoke_rejects_unexpired_dat` + Rust interop                                     | integration             | 403             | `DEVICE_REVOKED`                                | pass   |
| DAT expirado / kid / iss / aud / token_type  | `Forged_dat_claim_matrix_is_strictly_validated`                                            | unit/integration matrix | n/a → validator | `DEVICE_TOKEN_EXPIRED` / `DEVICE_TOKEN_INVALID` | pass   |
| DAT firma incorrecta (otro HMAC)             | `Dat_signed_with_different_key_material_and_current_kid_is_invalid`                        | unit                    | n/a             | `DEVICE_TOKEN_INVALID`                          | pass   |
| DAT security stamp anterior                  | revoke / disable / rebind E2E                                                              | integration             | 403             | stamp / terminal / revoked codes                | pass   |
| Terminal ausente / disabled                  | `Challenges_reject_missing_or_disabled_terminal_*`                                         | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| binding corrupto                             | `Tokens_reject_corrupt_terminal_binding`                                                   | integration             | 401             | `DEVICE_PROOF_INVALID`                          | pass   |
| wrong Tenant / Branch user claim             | `Operational_routes_reject_user_tenant_or_branch_mismatch_with_valid_dat`                  | integration             | 403             | `USER_BRANCH_MISMATCH`                          | pass   |
| valid DAT + invalid user                     | `Operational_routes_reject_invalid_user_jwt_with_valid_dat`                                | integration             | 401             | `USER_AUTH_REQUIRED` (or auth fail)             | pass   |
| valid user + invalid DAT                     | `Operational_routes_reject_invalid_dat_with_valid_user_jwt`                                | integration             | 401             | `DEVICE_AUTH_REQUIRED`                          | pass   |
| PG unavailable + cache miss/expired          | `DeviceAuthCacheFailureTests` + `Sales_path_returns_503_*`                                 | unit + integration      | 503             | `DEVICE_STATUS_UNAVAILABLE`                     | pass   |

---

## 5. Rate limiting (GATE 2)

Implemented in `BranchDeviceAuthRateLimiter` + `Program.cs` `GlobalLimiter` (CreateChained):

- Partitions: **global** + **IP** + **normalized DeviceId**
- Invalid DeviceId → shared `invalid` bucket (no unbounded partitions)
- Middleware buffers POST body and captures DeviceId into `HttpContext.Items` before the limiter
- 429 Problem Details `RATE_LIMITED` + `Retry-After`; no device lifecycle leakage
- Limits configurable: `IpPermitLimit` / `DevicePermitLimit` / `GlobalPermitLimit` / `RateLimitWindowSeconds` (+ `MachinePermitLimit` compat)

Tests (`DeviceAuthRateLimitTests`): same IP+device, same IP+different devices, different IPs+same device, global, invalid DeviceId bucket, window reset, anti-leak body.

---

## 6. Interop Rust → Kestrel → PostgreSQL (GATE 3)

| Item        | Value                                                                                                             |
| ----------- | ----------------------------------------------------------------------------------------------------------------- |
| test class  | `DeviceAuthRustProductInteropTests`                                                                               |
| test name   | `Rust_product_client_uses_dat_for_operational_route_and_observes_revocation`                                      |
| binaries    | `pairing_interop`, `device_auth_interop` (product code paths)                                                     |
| flow        | PG → Kestrel Branch → pairing → DAT issue → `/me` → Dev+User route → revoke → same DAT → `403 DEVICE_REVOKED`     |
| skip/ignore | none (`[Fact]`)                                                                                                   |
| CI          | `.github/workflows/desktop.yml` builds interop bins; backend IntegrationTests job runs this Fact against Postgres |
| focused run | exit 0 within DeviceAuth filter suite (~39s for 56 related tests)                                                 |

---

## 7. Revocation of live DAT

Covered by E2E revoke / disable / rebind and Rust interop step 12–14. Status cache evicted on admin mutations; stamp bump invalidates minted DATs.

---

## 8. Cache and PostgreSQL failure (GATE 6)

Policy (approved):

```text
cache válida y no expirada → usable durante caída temporal de PG
cache ausente o expirada → fail closed → 503 DEVICE_STATUS_UNAVAILABLE
no stale-while-error
```

Evidence:

- Unit: hit with disposed DB; miss/expired → `DEVICE_STATUS_UNAVAILABLE`
- `Evict_removes_the_device_status_snapshot`
- Integration: `Sales_path_returns_503_when_device_status_cache_miss_is_unavailable`

---

## 9. Desktop lifecycle and single-flight (GATE 4)

Rust tests in `device_auth/session.rs` (18+ cases), including:

restart clears DAT; first issue; concurrent single-flight; renew &lt;60s skew; 401 retry-once; revoke no retry; BranchInstance mismatch; Branch URL change; credentials missing; cancel releases flight; leader failure propagates + retry after failure; DAT not in envelope/keyring path; Debug/Display hygiene.

Public states `Authenticating` / `DeviceSessionFailed` are sanitised (code + message only; no DAT, signatures, bodies).

---

## 10. Anti-enumeration (GATE 5)

**`/challenges` (Active-only mint):** unknown, PendingConfirmation, Revoked, missing/disabled terminal → same public `DEVICE_PROOF_INVALID`. No usable challenge for non-active devices.

**`/tokens`:** proof failures (wrong key, wrong device, bad fingerprint path, malformed) → `DEVICE_PROOF_INVALID`; expired/replay have dedicated codes without internal entity dumps.

**DAT validation (`/me`, operational):** Revoked → `DEVICE_REVOKED` (after authentication of a previously issued DAT).

Theory `Public_device_auth_failures_are_indistinguishable` compares public `code`/`status`/`title` (ignores per-request instance ids).

---

## 11. Migration and backfill (GATE 7)

| Check                         | Result                                                                                                              |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `has-pending-model-changes`   | clean (exit 0)                                                                                                      |
| backfill                      | SQL assigns non-empty unique `security_stamp` for legacy rows                                                       |
| indexes                       | `device_auth_challenges` status+device and expires indexes present                                                  |
| Down                          | supported: drops challenges table + `security_stamp` column                                                         |
| schema                        | shared Platform EF model; Cloud Runtime does not register DeviceAuth endpoints/services → no Cloud behaviour change |
| atomic consume post-migration | E2E parallel redeem / replay tests                                                                                  |

---

## 12. OpenAPI final (GATE 8)

| Route                                 | request                            | response                      | security          | 429 | 503 | Problem Details |
| ------------------------------------- | ---------------------------------- | ----------------------------- | ----------------- | --- | --- | --------------- |
| POST `/branch/device-auth/challenges` | `CreateDeviceAuthChallengeRequest` | `DeviceAuthChallengeResponse` | anon + rate limit | yes | yes | yes             |
| POST `/branch/device-auth/tokens`     | `IssueDeviceAuthTokenRequest`      | `DeviceAuthTokenResponse`     | anon + rate limit | yes | yes | yes             |
| GET `/branch/device-auth/me`          | —                                  | `DeviceAuthMeResponse`        | **DeviceBearer**  | —   | yes | yes             |

Dev+User operational (Branch default OpenAPI `/openapi/v1.json`): single security requirement containing **UserBearer + DeviceBearer** (AND, not OR). Contract: `Branch_default_document_composes_user_and_device_security_for_sales`.

- Branch artifact reproducible via `BINEXUS_UPDATE_OPENAPI=1` + `DevicePairingOpenApiContractTests`
- Cloud `binexus-v1.json`: **no** `DeviceBearer` / `device-auth` (restored to HEAD after Api builds)
- No secret examples in OpenAPI

---

## 13. Config validation and secret scan (GATE 9)

| Check                                                           | Result                                                                                                   |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Lab/dev keys in `appsettings.Development.json` / `Testing.json` | marked `LabOnly: true`; fictional prefixes                                                               |
| Production/Staging                                              | validator rejects `LabOnly` / lab markers on current key                                                 |
| DAT ≠ Jwt                                                       | UTF-8 byte compare (+ SHA-256 equality guard) in `BranchDeviceAuthOptionsValidator`                      |
| Versioned secret scan                                           | DAT lab strings only under Dev/Testing appsettings (expected); not in OpenAPI/docs/fixtures as live keys |

---

## 14. Full regression (GATE 10)

| command                                                 | exit code    | test count / note                                                  | duration       | result            |
| ------------------------------------------------------- | ------------ | ------------------------------------------------------------------ | -------------- | ----------------- |
| `dotnet build apps/backend/Binexus.slnx`                | 0            | —                                                                  | ~16.6 s        | pass              |
| `dotnet test apps/backend/Binexus.slnx --no-build`      | 0            | Unit 143 + Integration 244 + Architecture 48 + Workers 6 = **441** | ~280 s         | pass              |
| `dotnet format … --verify-no-changes`                   | 0            | after one apply pass                                               | ~36–82 s       | pass              |
| `dotnet ef migrations has-pending-model-changes`        | 0            | no pending                                                         | ~22 s          | pass              |
| `cargo fmt --check` (src-tauri)                         | 0            | —                                                                  | ~1.7 s         | pass              |
| `cargo clippy --workspace --all-targets -- -D warnings` | 0            | —                                                                  | ~84 s          | pass              |
| `cargo test --workspace --all-targets`                  | 0            | **55** lib tests (+ bins 0)                                        | ~12 s          | pass              |
| `pnpm install --frozen-lockfile`                        | 0            | —                                                                  | ~2.7 s         | pass              |
| `pnpm --filter @binexus/desktop test`                   | 0            | package tests                                                      | ~25 s          | pass              |
| `pnpm --filter @binexus/desktop build`                  | 0            | Vite build                                                         | ~21 s          | pass              |
| Branch OpenAPI regenerate + match                       | 0            | contract Fact                                                      | ~2 s test      | pass              |
| Cloud OpenAPI without DeviceAuth                        | 0 hits       | restored                                                           | —              | pass              |
| `dotnet list package --vulnerable`                      | 0            | no High/Critical reported                                          | ~27 s          | pass              |
| `cargo audit` raw                                       | 1            | 3 vulns (see §15)                                                  | ~8 s           | expected raw fail |
| `cargo audit` + policy exceptions                       | 0            | RUSTSEC-2026-0194/0195/0009 via exceptions file                    | CI desktop job | gate accepted     |
| secret scan                                             | 0 unexpected | lab keys only in Dev/Testing                                       | —              | pass              |

DeviceAuth-focused filter (integration): **56 passed**, ~39 s.

---

## 15. Audits

**NuGet:** no vulnerable packages reported on current sources.

**cargo audit:** do not report as PASS. Raw `cargo audit` exits 1 (3 vulnerabilities: `quick-xml`×2 high DoS, `time` medium DoS) plus allowed/unmaintained warnings. Gate accepted only via current policy: `.github/workflows/desktop.yml` + `docs/migration/cargo-audit-exceptions-desktop.json` for `RUSTSEC-2026-0194`, `RUSTSEC-2026-0195`, `RUSTSEC-2026-0009` (each keeps dependency path, impact, mitigation, owner, reviewBy, removalCondition). Exceptions were not expanded in this initiative.

---

## 16. Riesgos reales pendientes

1. **HMAC DAT key = forge capability** (known PLAN limitation until asymmetric DAT or HSM).
2. **No LAN TLS / pinning** (explicitly out of scope; `AllowInsecureBranchTransport` lab-only).
3. **cargo audit High transitive** (`quick-xml`) — handled by desktop exception policy until upstream bump.
4. **Rebind = rename-in-place** (unique DeviceId index); product docs must not promise multi-terminal bind without schema change.
5. **Rate-limit GlobalLimiter** applies to all hosts; non-device-auth paths use NoLimiter partitions (verify under production load that chain cost stays negligible).
6. **Working tree still includes pairing/desktop noise and spikes** — prune before commit split.

---

## 17. Propuesta final de commits compilables

Suggested order (each builds/tests in isolation):

1. `feat(platform): add Branch device-auth domain, migration, and DAT services`
2. `feat(api): wire device-auth endpoints, OpenAPI, and chained rate limits`
3. `feat(modules): require BranchDeviceAndUser on operational Branch routes`
4. `test(backend): device-auth HTTP matrix, rate limit, cache, migration, interop`
5. `feat(desktop): RAM DAT session, IPC states, and device_auth_interop harness`
6. `test(desktop): device_auth lifecycle and single-flight coverage`
7. `docs(branch-device-auth): PLAN + PR-ready checkpoint; regenerate branch OpenAPI artifact`

Do **not** include: `spikes/target/**`, accidental Cloud OpenAPI churn, local `.tmp-*.log`.

---

## Stop

No `git add` / `git commit` / `git push` / `gh pr create` performed. Await human approval of commit split.
