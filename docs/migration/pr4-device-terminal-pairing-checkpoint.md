# CHECKPOINT PR 4 — DEVICE AND TERMINAL PAIRING BACKEND

**Status:** Implementation complete (uncommitted on `feat/branch-device-pairing`)
**Date:** 2026-07-17
**Base:** `abc602e` (PR 3 merged)
**Scope:** Branch-only device/terminal pairing ceremony with cryptographic proof of possession + explicit admin approval. Backend, wire contracts, tests, docs. No Tauri, LAN TLS, mTLS, mDNS, discovery, sync, installer, hardware, or operational Device auth.

---

## OpenAPI Branch spike — result: GO

The spike succeeded. The `branch-v1` document renders as a **pairing-only** surface and is served at runtime (`GET /openapi/branch-v1.json`) when `RuntimeMode=Branch`.

Key finding (the conflict flagged in the plan): `WithGroupName("branch-v1")` alone is **not** enough. The default document predicate is `GroupName == null || GroupName == documentName`, so ungrouped endpoints (e.g. `/health`) leaked into `branch-v1`. Fix: set a document-scoped `options.ShouldInclude = api => api.GroupName == "branch-v1"`. `ExcludeFromDescription()` was **not** used (it would delete the route from every document).

- Cloud `v1` excludes all pairing routes by default (group mismatch) — no code change to the Cloud doc.
- Branch `branch-v1` contains exactly the 11 pairing routes (admin + machine), DTOs, and response codes; no shared/cloud endpoints; no real code/receipt/status-token baked into examples.
- Artifact `artifacts/openapi/binexus-branch-v1.json` is produced by the runtime contract test (`DevicePairingOpenApiContractTests`), not by build-time generation.

**Documented blocker for PR 5:** build-time `OpenApiGenerateDocumentsOnBuild` always runs in Cloud mode, so it cannot emit `binexus-branch-v1.json`. A dedicated Branch-mode generation host (or `dotnet run --runtime Branch -- --openapi`) is the PR 5 follow-up. Until then the artifact + contract tests are the source of truth.

## Final tables and constraints

Five new tables, all `Platform` (Branch runtime), UUIDv7 PKs, `timestamptz`, `xmin` concurrency token.

| Table                       | Purpose / status column                                                           | Key constraints                                                                                                                                                        |
| --------------------------- | --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `device_pairing_sessions`   | Admin-minted code session — `Open / Consumed / Expired`                           | `code_hash` unique; check on status; index `(branch_instance_id, status)`                                                                                              |
| `device_pairing_challenges` | Single-use PoP — `phase = Exchange / Confirmation`                                | phase check; phase-target check (exchange⇒session, confirm⇒request+receipt hash); index `(branch_instance_id, expires)`                                                |
| `device_pairing_requests`   | Awaiting approval — `PendingApproval / Approved / Rejected / Expired / Completed` | `(pairing_session_id, device_id)` unique; `status_token_hash`; `pairing_receipt_hash`                                                                                  |
| `branch_devices`            | Permanent local identity — `PendingConfirmation / Active / Revoked`               | `(branch_instance_id, public_key_fingerprint)` **unique incl. Revoked**; `(branch_instance_id, credential_hash)` **unique incl. Revoked**; `pairing_request_id` unique |
| `branch_terminals`          | Workstation (`Caja 1`) — `PendingConfirmation / Active / Disabled`                | `device_id` unique; `(branch_instance_id, normalized_name)` unique **filtered to `PendingConfirmation`+`Active`**                                                      |

All hashes are `varchar(64)` (SHA-256 hex). `public_key` `varchar(512)`, names `varchar(50)`.

## Pairing code

8 numeric digits. CSPRNG with rejection sampling (no modulo bias). Normalized (trim) before hashing. `HMAC-SHA256(CodePepper, normalizedCode)` stored as `code_hash` (unique). Fixed-time comparison. Never logged. Pepper is Branch-only config, required (min 32 chars), distinct from the Cloud activation pepper; the development-only value is refused outside Development.

## Exchange challenge (Option A — anonymous-exhaustion mitigation)

`POST /branch/pairing/challenges` requires `pairingCode` (plus `pairingSessionId`, `deviceId`, `publicKey`, `credentialHash`). The code is verified **without consuming the session**; failed attempts + lockout apply; a challenge is minted only if the code is valid. All failures return `PAIRING_INVALID` (no oracle revealing whether a session exists). The returned challenge carries `branchInstanceId` + `nonce` so the device can build the canonical signed payload. Exchange re-validates the session + signature.

**Failure counting:** the counter is incremented at most once per logical attempt, at code verification in the challenge request. A successful step resets it. Exchange itself does not double-count the same code attempt.

## Admin fingerprint approval

Exchange creates only a `PairingRequest` (no Device/Terminal yet). Admin sees `deviceFingerprintShort` in `GET /branch/pairing/requests/{id}` — the exact display format PR 5 Tauri will render: 12 hex chars grouped as `A1B2-C3D4-E5F6` (display-only; all crypto uses the full 64-char SHA-256). A stolen code alone cannot activate a device: approval is a separate human decision.

## Request status-token rotation

Server stores only `status_token_hash`, so it cannot re-serve a lost token. Deterministic policy on an **exact** exchange retry (same session, DeviceId, public key, credential hash, fresh valid PoP): find the same `PairingRequest`, mint a **new** `pairingStatusToken`, atomically replace `status_token_hash` (invalidating the previous one), return the **same** `pairingRequestId`. The device does not regenerate its key pair or credential. Any other DeviceId/key/credential ⇒ `PAIRING_INVALID`. Code alone (no PoP) cannot rotate the token after the first exchange. Concurrent retries are serialized (row lock) ⇒ exactly one live token.

## Approval / rejection

- Approve: mints `BranchDevice` (`PendingConfirmation`) + `BranchTerminal` (`PendingConfirmation`) + a `Confirmation` challenge, moves request to `Approved`. Idempotent — repeat returns the same `terminalId`/`deviceId`.
- Reject: request ⇒ `Rejected`; if it had been approved, the `PendingConfirmation` Device ⇒ `Revoked` and Terminal ⇒ `Disabled`; status token, receipt, and confirmation challenge invalidated.
- `Approved` never returns to `PendingApproval`.

## Confirmation challenge

Issued only after approval, separate from the exchange challenge. The device signs a canonical payload (length-prefixed UTF-8, version `binexus-device-pairing-confirm-v1`) binding: `confirmationChallengeId, pairingRequestId, branchInstanceId, deviceId, terminalId, publicKeyFingerprint, credentialHash, pairingReceiptHash (SHA-256 of the raw receipt), nonce, expiresAtUtc`. The `pairingStatusToken` is validated **separately** by hash and is never part of the permanent crypto material.

## Receipt raw/hash policy

Branch generates a high-entropy (≥256-bit) `pairingReceipt` **raw** at approval. It is held once in an in-memory vault (`IPairingReceiptVault`), delivered exactly once as `pairingReceipt` on the first successful `Approved` status poll, and only `SHA-256(pairingReceipt)` is persisted (`pairing_receipt_hash`). The raw receipt is temporal, is not the permanent credential, is never logged, never appears in admin listings, and is invalidated on `Completed / Rejected / Expired`. The DTO field is `pairingReceipt` (raw), never `pairingReceiptHash`.

## Confirm without credential raw

Confirm sends `pairingReceipt` (+ signature + ids), never the permanent device credential. Branch: hashes the receipt, fixed-time compares against `pairing_receipt_hash`, verifies the ECDSA signature bound to that hash, then activates Device + Terminal and marks the request `Completed`. The permanent credential hash is set at approval from the device-declared value and only ever stored as a hash.

## Expiration and authoritative clock

Lazy transactional expiry (no sweeper). **`PostgreSQL NOW()` is authoritative** for session/request/challenge expiry, lockout (`locked_until`), and concurrent cleanup — consistent with the Outbox policy. `TimeProvider` is used only for application-recorded timestamps, DTOs, and unit tests. The two clocks are never mixed inside one concurrency comparison.

## Idempotency

- Lost exchange response ⇒ retry returns the same request, rotates the status token (see above).
- Lost approval ⇒ re-approve returns the same device/terminal/challenge.
- Lost confirm ⇒ re-confirm returns `alreadyActive = true`.
- Wrong key/hash on any retry ⇒ `PAIRING_INVALID`.

## Concurrency matrix (real PostgreSQL tests)

| Race                        | Outcome                                                                             |
| --------------------------- | ----------------------------------------------------------------------------------- |
| approve vs approve          | exactly one terminal/device; both callers get the same `terminalId`                 |
| approve vs reject           | exactly one transition wins                                                         |
| confirm vs confirm          | device `Active` once; the loser sees `alreadyActive`                                |
| confirm vs revoke           | confirm-first ⇒ Active then Revoked; revoke-first ⇒ confirm fails `PAIRING_INVALID` |
| expiry vs confirm           | expired-by-authoritative-clock ⇒ confirm fails + cleanup; else `Completed` wins     |
| concurrent exchange retries | serialized ⇒ single live status token                                               |

## Revocation and no-reuse policy

`POST /branch/devices/{deviceId}/revoke` (admin JWT, Branch-only, idempotent) sets Device ⇒ `Revoked`, associated Terminal ⇒ `Disabled`, records actor + timestamp, keeps history, does not delete. Revocation is **terminal** for the cryptographic material: `DeviceId` (PK), `publicKeyFingerprint`, and `credentialHash` are unique per Branch instance **including Revoked rows**, so they can never be re-registered. Re-pairing requires a fresh DeviceId + key pair + credential. The **only** value freed for reuse is the **terminal name** (unique filtered to `PendingConfirmation`+`Active`; `Disabled` frees it).

## Sales compatibility

No change to Sales. `Sales` uses a free-form `string TerminalId` (1..N chars, normalized) with no FK to `branch_terminals`. PR 4 does not couple Sales to the canonical `branch_terminals.Id` (UUIDv7); mapping is a future decision. Verified by architecture tests (commercial modules must not reference `Platform.Branching.Pairing`).

## Runtime isolation

Pairing endpoints and services register/map only when `RuntimeMode=Branch`. Cloud host ⇒ `/branch/pairing/*` returns 404 (endpoints never mapped). Branch requires `BranchInstance.Status = Active`; `ReadyForActivation` ⇒ `BRANCH_NOT_ACTIVE` (409). Architecture tests assert the pairing services are not registered in Cloud runtime.

## Configuration

`BranchPairing` typed options (`DevicePairingOptions`, `ValidateOnStart` outside the OpenAPI generation host): `CodePepper` (required, ≥32, dev value refused outside Development), session/request/challenge TTLs, max failed attempts, lockout duration, admin/machine rate-limit permit counts. `appsettings.Development.json` and `.env.example` updated. Rate-limit policies `branch-pairing-admin` (per tenant+user) and `branch-pairing-machine` (per IP) added.

## Migrations and idempotent SQL

- Migration `20260717072639_Platform_BranchDevicePairing` (5 tables, checks, unique + partial indexes).
- `apps/backend/db/binexus-idempotent.sql` regenerated (+196 lines, additive only).
- `dotnet ef migrations has-pending-model-changes`: **none**.

## Tests

| Suite            | Passed | Notes                                                                                                               |
| ---------------- | ------ | ------------------------------------------------------------------------------------------------------------------- |
| Unit             | 120    | code/secret/fingerprint/canonical codec + entity state machines + options validator                                 |
| Architecture     | 48     | module isolation, Platform placement, DI lifetimes (Branch vs Cloud), OpenAPI no-leak                               |
| Workers.Tests    | 6      | unchanged                                                                                                           |
| Integration      | 184    | full ceremony, attacker-rejected, token rotation, no-reuse, runtime isolation, concurrency, OpenAPI branch contract |
| Failed / skipped | 0 / 0  |                                                                                                                     |

## OpenAPI Cloud unchanged

`artifacts/openapi/binexus-v1.json` restored after build; `git status` shows it unmodified. Architecture test `Cloud_openApi_artifact_never_exposes_device_pairing` asserts no `/branch/pairing`, `/branch/devices`, `/branch/terminals` routes in the Cloud doc.

## OpenAPI Branch or documented blocker

Both: runtime artifact `artifacts/openapi/binexus-branch-v1.json` (new) **and** contract tests. Build-time emission deferred to PR 5 (see spike result).

## Verification

| Check                    | Result                                                                         |
| ------------------------ | ------------------------------------------------------------------------------ |
| Restore / build (Debug)  | green, 0 warnings, 0 errors                                                    |
| Full test run            | 358 passed, 0 failed, 0 skipped                                                |
| NuGet vulnerable         | 0 across all 18 projects (`dotnet list ... --vulnerable --include-transitive`) |
| EF pending model changes | none                                                                           |
| OpenAPI Cloud            | unchanged (restored)                                                           |
| OpenAPI Branch           | 11 pairing routes, pairing-only                                                |

## Transport security (documented limitation)

> Pairing is designed for a trusted local LAN during this phase. Confidentiality is not guaranteed until LAN TLS is implemented.

Even though the raw permanent credential never crosses the network, the pairing code, public key, DeviceId, terminal name, status token, pairing receipt, and signatures do. Visual approval prevents silent takeover but does not protect against metadata observation, denial of service, or capture of a status token/receipt. All fields that influence binding are inside the signed canonical payloads.

## Git status (working tree, uncommitted)

Modified: `.env.example`, `apps/backend/src/Binexus.Api/Program.cs`, `apps/backend/src/Binexus.Api/appsettings.Development.json`, `apps/backend/src/Binexus.Platform/DependencyInjection/BranchRuntimeServiceCollectionExtensions.cs`, `apps/backend/src/Binexus.Platform/Persistence/BinexusDbContext.cs`, `apps/backend/src/Binexus.Platform/Persistence/Migrations/BinexusDbContextModelSnapshot.cs`, `apps/backend/db/binexus-idempotent.sql`, `apps/backend/tests/Binexus.ArchitectureTests/Branching/BranchHealthOpenApiTests.cs`.

New: `Branching/Crypto/*` (5), `Branching/Configuration/*` (2), `Branching/Contracts/*` (3), `Branching/Persistence/*` (6 incl. configurations), `Branching/Application/*` (3), `Branching/Pairing/*`, `Hosting/BranchDevicePairingEndpointExtensions.cs`, migration `20260717072639_*` (+Designer), 4 test files, `artifacts/openapi/binexus-branch-v1.json`, this doc.

## Proposed commits

1. `feat(backend): add device/terminal pairing crypto and contracts` — crypto formats, pairing code/secret/fingerprint, canonical codec, options + validator, wire DTOs + service interfaces.
2. `feat(backend): add device pairing entities and migration` — 5 entities + EF configurations, DbSets, migration, snapshot, idempotent SQL.
3. `feat(backend): add branch device pairing ceremony and admin services` — machine + admin services, receipt vault, endpoints, DI, rate limits, `Program.cs` wiring, appsettings/.env.
4. `test(backend): cover device pairing ceremony, concurrency and OpenAPI` — unit, architecture, integration, OpenAPI contract tests.
5. `docs(migration): add PR 4 device/terminal pairing checkpoint` — this doc.

(Single squashed `feat(backend): add device/terminal pairing backend (#NN)` is also fine per repo history.)

## Exact Git commands (you run these)

```bash
git add apps/backend docs/migration/pr4-device-terminal-pairing-checkpoint.md .env.example artifacts/openapi/binexus-branch-v1.json
git commit -m "feat(backend): add device/terminal pairing backend"
git push -u origin feat/branch-device-pairing
gh pr create --title "feat(backend): add device/terminal pairing backend" --body "$(...)"   # paste the PR body block below
```

(Verify `git status` shows `artifacts/openapi/binexus-v1.json` unmodified before committing. Paste the "PR body" block below into `gh pr create`, or save it to a file and use `--body-file`.)

## PR body (repository template)

```markdown
## What

- Add Branch-only device/terminal pairing: 8-digit code session, ECDSA P-256 proof of possession, explicit admin fingerprint approval, one-time raw pairing receipt, separate confirmation challenge, permanent device credential (hash-only on server), and minimal revocation.
- Add 5 pairing tables + migration, machine/admin endpoints, `branch-v1` OpenAPI document, and full unit/architecture/integration/concurrency tests.

## Why

Lets a Branch Client securely pair with an Active Branch Server and obtain a device credential for future LAN requests, without exposing the raw credential and without trusting the pairing code alone.

Closes #

## How

Pairing ceremony: admin session → device challenge (code-gated) → signed exchange → admin approval (mints PendingConfirmation Device+Terminal + confirmation challenge + raw receipt) → device confirm (signature + receipt) → Active. `PostgreSQL NOW()` is authoritative for expiry/lockout; revocation is terminal for DeviceId/fingerprint/credentialHash; terminal names are reusable once Disabled.

## Affected areas

- [x] `apps/backend/` (.NET)
- [ ] `apps/web`
- [ ] `apps/desktop`
- [ ] `packages/`
- [ ] `infrastructure/`
- [x] `docs/`

## Bounded context(s)

- [x] cross-cutting / foundation

## Checklist

- [x] Conventional Commit title
- [ ] `pnpm exec turbo run typecheck lint build` (no frontend changes)
- [x] `dotnet test apps/backend/Binexus.slnx` green (358 passed)
- [x] State machine changes documented (this checkpoint)
- [x] Multi-tenant: pairing entities scoped by BranchInstance (Branch runtime)
- [x] No secrets committed (raw receipt/code/token never persisted or logged)

## Out of scope / follow-ups

Tauri pairing UI, LAN TLS/mTLS, mDNS/discovery, sync, installer, hardware, operational Device auth, build-time `binexus-branch-v1.json` generation host, mapping `branch_terminals.Id` into Sales.
```

## Out of scope

Tauri UI, wizard, mDNS, discovery, LAN TLS, mTLS, PIN login, hardware, sync, installer, auto-update, Cloud administration of devices, multi-branch devices, a client-side credential store in the backend (only `SimulatedPairingClient` in tests).
