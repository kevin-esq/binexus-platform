# CHECKPOINT — BRANCH DEVICE AUTHENTICATION VALIDATION

**Status:** Validation gates advanced; **not** approved for commit split until remaining gaps below are closed.
**Branch:** `feat/branch-device-auth`
**Base:** `origin/main` @ `2c95236`
**Parent:** [branch-operational-security.md](../architecture/branch-operational-security.md)
**PLAN:** [PLAN.md](./PLAN.md)
**Commits / push / PR:** none (blocked by instruction)

---

## 1. Branch and base

| Item         | Value                                    |
| ------------ | ---------------------------------------- |
| Branch       | `feat/branch-device-auth`                |
| Base         | `origin/main` (`2c95236`)                |
| Working tree | dirty (implementation + validation work) |

## 2. `git status --short` (summary)

~74 paths changed/untracked spanning Platform DeviceAuth, Api OpenAPI transformer, modules policies, desktop `device_auth`, fixtures, docs, `artifacts/openapi/binexus-branch-v1.json`.
`artifacts/openapi/binexus-v1.json` restored to HEAD (Cloud without intentional device-auth surface).

## 3. Diff classified

| Area               | Contents                                                                                     |
| ------------------ | -------------------------------------------------------------------------------------------- |
| Docs               | PLAN + parent security + this VALIDATION checkpoint                                          |
| Backend DeviceAuth | challenges, DAT issuer/validator, stamp, cache, Problem Details, auth challenge handler      |
| Admin lifecycle    | revoke + **disable terminal** + **rebind terminal** (rename-in-place; unique DeviceId index) |
| OpenAPI            | `branch-v1` device-auth routes + `UserBearer`/`DeviceBearer` + regenerated artifact          |
| Modules            | `RequireOperationalAuthorization` on Sales/Inventory/Orders/Warehouse/Logistics              |
| Desktop            | DAT RAM session, public states, IPC commands, `device_auth_interop` bin, golden fixture      |
| Tests              | E2E 21 DeviceAuth integration + unit options/cache/codec/golden                              |

---

## 4. OpenAPI contracts (`branch-v1`)

| method | route                            | security                                            | request schema                                                                    | response schema               | error responses              | contract test                            |
| ------ | -------------------------------- | --------------------------------------------------- | --------------------------------------------------------------------------------- | ----------------------------- | ---------------------------- | ---------------------------------------- |
| POST   | `/branch/device-auth/challenges` | anon + rate limit                                   | `CreateDeviceAuthChallengeRequest` (`deviceId` uuid)                              | `DeviceAuthChallengeResponse` | 400,401,429,503 problem+json | `DevicePairingOpenApiContractTests`      |
| POST   | `/branch/device-auth/tokens`     | anon + rate limit                                   | `IssueDeviceAuthTokenRequest` (challengeId, deviceId, signature, protocolVersion) | `DeviceAuthTokenResponse`     | 400,401,403,409,429,503      | same                                     |
| GET    | `/branch/device-auth/me`         | **DeviceBearer** (`X-Binexus-Device-Authorization`) | —                                                                                 | `DeviceAuthMeResponse`        | 401,403,503                  | same (+ DeviceBearer/UserBearer schemes) |

Also documented admin: `POST /branch/terminals/{terminalId}/disable`, `POST /branch/devices/{deviceId}/terminals/rebind`.

- Artifact regenerated via `$env:BINEXUS_UPDATE_OPENAPI=1` + contract test (not hand-edited).
- Cloud `binexus-v1.json`: no `device-auth` / `DeviceBearer` (restored).
- No DAT/nonce/signature examples in Branch artifact assertions.

---

## 5. Route / policy matrix

| Runtime | Module routes                                  | Policy                                                |
| ------- | ---------------------------------------------- | ----------------------------------------------------- |
| Branch  | Sales, Inventory, Orders, Warehouse, Logistics | `BranchDeviceAndUser` (JwtBearer + DeviceAccessToken) |
| Cloud   | same modules                                   | default User JWT only                                 |
| Branch  | `/branch/device-auth/me`                       | `BranchDeviceOnly`                                    |
| Branch  | challenges/tokens                              | Anonymous + `branch-device-auth` rate limit           |

Composition via host metadata — not `if (RuntimeMode)` in domain handlers.

---

## 6. Test matrix (identifiable coverage)

### Integration (`DeviceAuthEndToEndTests` + OpenAPI) — **21 passed**

| scenario                                 | test name                                                                 | layer | expected HTTP              | expected code                             | result |
| ---------------------------------------- | ------------------------------------------------------------------------- | ----- | -------------------------- | ----------------------------------------- | ------ |
| Active → DAT → /me → revoke → reject     | `Issue_me_revoke_rejects_unexpired_dat`                                   | int   | 200 then **403**           | DEVICE_REVOKED                            | PASS   |
| Disable terminal invalidates DAT         | `Disable_terminal_invalidates_live_dat_via_stamp`                         | int   | 403/401                    | TERMINAL_DISABLED or TOKEN_INVALID        | PASS   |
| Rebind invalidates; new DAT works        | `Rebind_terminal_invalidates_live_dat_and_allows_new_dat`                 | int   | stale fail; fresh 200      | stamp                                     | PASS   |
| Challenge replay                         | `Challenge_replay_is_rejected_atomically`                                 | int   | 409                        | DEVICE_CHALLENGE_REPLAYED                 | PASS   |
| Parallel redeem one winner               | `Parallel_redeem_of_same_challenge_yields_exactly_one_dat`                | int   | 200+409                    | REPLAYED                                  | PASS   |
| Ignore client credentialHash             | `Tokens_body_does_not_accept_client_credential_hash_as_trust_input`       | int   | 200                        | —                                         | PASS   |
| Unknown device anti-enum                 | `Unknown_device_challenge_is_generic_proof_invalid`                       | int   | 401                        | DEVICE_PROOF_INVALID                      | PASS   |
| Cloud hides device-auth; Sales user-only | `Cloud_runtime_keeps_user_only_sales_and_hides_device_auth`               | int   | 404 / not DEVICE_AUTH      | —                                         | PASS   |
| Branch Sales Dev+User                    | `Branch_sales_requires_device_and_user`                                   | int   | 401 without DAT            | DEVICE_AUTH_REQUIRED                      | PASS   |
| Five modules HTTP Dev+User               | `Branch_operational_modules_require_device_and_user` (Theory×5)           | int   | 401 / 401 / not auth codes | DEVICE_AUTH_REQUIRED / USER_AUTH_REQUIRED | PASS   |
| OpenAPI lock                             | `Branch_document_is_complete_reproducible_and_matches_committed_artifact` | int   | —                          | —                                         | PASS   |

### Unit — **11 passed** (DeviceAuth\* + OptionsValidator)

| scenario                        | test                                                                  | layer | result |
| ------------------------------- | --------------------------------------------------------------------- | ----- | ------ |
| Forged DAT iss/aud/type/kid/exp | `DeviceAuthSecurityMatrixTests.Forged_dat_claim_matrix_*`             | unit  | PASS   |
| Malformed DAT                   | `Malformed_dat_is_device_token_invalid`                               | unit  | PASS   |
| Boot options matrix             | `BranchDeviceAuthOptionsValidatorTests`                               | unit  | PASS   |
| Cache key contract              | `DeviceAuthCacheTests`                                                | unit  | PASS   |
| Codec + golden C#↔Rust          | `DeviceAuthCanonicalCodecTests`, `DeviceAuthGoldenVectorInteropTests` | unit  | PASS   |

### Desktop Rust — **8 passed** (`cargo test --lib device_auth`)

IPC hygiene, clear/restart, single-flight (partial), named public states.

### Gaps still open vs full PLAN matrix (Blocker 2)

Not yet fully automated HTTP evidence for every row (examples): wrong private key HTTP, PendingConfirmation challenge, wrong BranchInstance PoP, rate-limit 429, PG-down 503 path, kid rotation HTTP, wrong Tenant/Branch user claims, expired challenge clock, product Rust→Kestrel DAT interop C# driver, full Blocker 14 regression suite (`format`/`clippy`/`pnpm`/`audit`).

---

## 7. DAT config / boot validation

`BranchDeviceAuthOptionsValidator` fails boot when:

- missing/unknown `CurrentKeyId`
- empty ring / duplicate kid
- key &lt; 32 UTF-8 bytes
- TTL / skew / cache ranges invalid
- DAT key equals `Jwt:SigningKey`

Evidence: `BranchDeviceAuthOptionsValidatorTests`.
**Config without Git secrets:** Dev keys in `appsettings.Development.json` (dev-only strings); Testing/CI in `appsettings.Testing.json` (non-prod lab keys); Production must inject via env/secret store (not committed).

---

## 8. Challenge atomicity

`ExecuteUpdate` Open→Consumed requiring 1 row + execution strategy — covered by replay + parallel tests.

## 9. Live DAT revocation

Covered: revoke → 403 `DEVICE_REVOKED`; disable → stamp bump + rejection; rebind → stamp bump + rejection + fresh DAT OK.
**Policy:** revoke checks device status **before** terminal binding codes.

## 10. Cache / fail-closed

| Rule                                              | Implemented                                                     |
| ------------------------------------------------- | --------------------------------------------------------------- |
| key = `device-auth:{BranchInstanceId}:{DeviceId}` | yes                                                             |
| TTL = `StatusCacheSeconds` (15 default)           | yes                                                             |
| revoke/disable/rebind eager Evict                 | yes                                                             |
| Valid non-expired cache **may** serve if PG fails | **yes (PLAN)** — miss/expired → `DEVICE_STATUS_UNAVAILABLE` 503 |
| No stale-while-error for Sales/Inventory          | yes (fail closed when no valid entry)                           |

**Gap:** dedicated integration forcing PG down still thin (unit key contract only).

## 11. Migration

`20260718093009_Platform_BranchDeviceAuth`: `security_stamp` + backfill UUID; `device_auth_challenges` + indexes; Down drops both.
`dotnet ef migrations has-pending-model-changes` → **No changes**.
Cloud schema unchanged beyond shared Platform tables (expected for Branch devices).

## 12–13. Desktop DAT + IPC

- RAM-only; `RENEW_SKEW=60s`; public kinds: DeviceAuthenticated / DeviceSessionExpired / DeviceRevoked / CredentialsUnavailable / BranchIdentityMismatch (+ Authenticating / DeviceSessionFailed).
- IPC commands return public state only.
- `device_auth_interop` binary exists (JSON events, no DAT print).
  **Gap:** C# `DeviceAuthRustProductInteropTests` driver vs Kestrel not landed; some 401/403 retry lifecycle Wiremock cases still thin.

## 14. Golden vectors

Fixture `apps/desktop/spikes/fixtures/device-auth-crypto-golden-v1.json` + `DeviceAuthGoldenVectorInteropTests` (C#↔Rust). Negatives partially covered in golden suite.

## 15. Product interop

Binary: `cargo build --bin device_auth_interop` OK.
**Missing gate:** automated Rust→Kestrel→PostgreSQL test class + CI job wiring.

## 16. Regression (Blocker 14) — partial this session

| Command                                     | Result                  |
| ------------------------------------------- | ----------------------- |
| `dotnet build` (Platform/Api/tests)         | PASS                    |
| `dotnet test` DeviceAuth filter             | PASS (21 int + 11 unit) |
| OpenAPI Branch regenerate                   | PASS                    |
| EF pending model changes                    | none                    |
| `cargo test --lib device_auth`              | PASS (8)                |
| `cargo build --bin device_auth_interop`     | PASS                    |
| `dotnet format --verify-no-changes`         | **not run**             |
| `cargo fmt/clippy -- -D warnings`           | **not run**             |
| `cargo test --workspace --all-targets`      | **not run**             |
| `pnpm --filter @binexus/desktop test/build` | **not run**             |
| NuGet/cargo audit / secret scan             | **not run**             |

## 17. Secrets / HMAC separation

| Check                                                      | Status                           |
| ---------------------------------------------------------- | -------------------------------- |
| `IDeviceAccessTokenIssuer` ≠ `IDeviceAccessTokenValidator` | yes (separate types)             |
| Modules do not take signing keys                           | yes (principals only)            |
| DAT keys not in Desktop / Cloud OpenAPI / IPC              | yes                              |
| Lab keys in Development/Testing appsettings                | documented lab-only              |
| HMAC limitation                                            | documented in options XML + PLAN |

## 18. Real remaining risks / blockers

1. Full PLAN security matrix rows still incomplete (HTTP negatives / 429 / PG 503).
2. Product interop C# harness missing.
3. Blocker 14 full regression not executed.
4. Rate limit is IP-partitioned; DeviceId partition not fully proven.
5. Rebind keeps TerminalId (rename-in-place) due to unique `DeviceId` index — stamp still invalidates DAT.
6. `AllowInsecureBranchTransport` warns; no hard HTTP reject (by D3).

## 19. Proposed commits (when gates close)

1. `docs(branch-device-auth): PLAN + parent security baseline`
2. `feat(backend): device-auth challenges, DAT, stamp, cache, OpenAPI`
3. `feat(backend): disable/rebind terminal + Dev+User policies`
4. `feat(desktop): DAT lifecycle + interop bin + golden fixture`
5. `test(backend/desktop): device-auth validation suites`
6. `docs(branch-device-auth): VALIDATION checkpoint`

Do **not** commit until remaining Blocker 2/9/14 rows are green and human review accepts this checkpoint.
