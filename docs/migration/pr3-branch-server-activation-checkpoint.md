# CHECKPOINT PR 3 — BRANCH SERVER ACTIVATION

**Status:** Implementation complete (uncommitted on `feat/branch-server-activation`)  
**Date:** 2026-07-15  
**Base:** `4d941eb` (PR 2 merged)  
**Scope:** Cloud activation ceremony + Branch finalize-to-Active. No Tauri, pairing, sync, DPAPI, installer, Replace, or mTLS.

---

## Crypto spike

Decision: **ECDSA P-256 + SHA-256** via `System.Security.Cryptography` only.  
Ed25519 rejected: no first-party cross-platform API in .NET 10; PR 3 adds no crypto NuGet package.  
Details: [`pr3-activation-crypto-spike.md`](pr3-activation-crypto-spike.md).

| Constant                  | Value                                                         |
| ------------------------- | ------------------------------------------------------------- |
| Algorithm                 | `ECDSA_P256_SHA256`                                           |
| PublicKeyFormat           | Base64Url(SubjectPublicKeyInfo DER)                           |
| SignatureFormat           | Base64Url(IEEE P1363)                                         |
| FingerprintFormat         | lowercase hex SHA-256 of SPKI DER                             |
| CanonicalChallengePayload | length-prefixed UTF-8, version `binexus-branch-activation-v1` |

## Branch-generated credential

Branch mints 32-byte CSPRNG installation token (Base64Url) once per logical attempt. Cloud stores **hash only**. Retries keep the same token/hash. Receipt may rotate while Reserved; token never auto-rotates.

Permanent local store retains: installation token raw, private key PKCS#8, public key/fingerprint.

## Activation code

`BNX-XXXXX-XXXXX` — Crockford Base32, 50 bits, TTL 20 minutes. HMAC-SHA256(pepper, normalized). No silent O→0 / I→1 remapping (reject illegal symbols). Pepper required Cloud Production/Staging (ValidateOnStart). Logs use `ActivationId` only.

## Tables

| Table                          | Purpose                                                                                      |
| ------------------------------ | -------------------------------------------------------------------------------------------- |
| `branch_activations`           | Open → Reserved → Consumed / Expired; code hash; receipt hash; attempts/lock                 |
| `cloud_branch_instances`       | Activating → Active; token hash; public key; binding                                         |
| `branch_activation_challenges` | Single-use PoP; bound to InstanceId + fingerprint + token hash                               |
| `branch_instances` (local)     | + TenantId, BranchId, ActivatedAtUtc, CloudActivationId; status ReadyForActivation \| Active |

No `branch_installation_credentials` history table. No reversible receipt ciphertext on Cloud.

## States / expiry

Lazy transactional expiry via `TimeProvider` timestamps. Reserved/Activating windows free unique partial indexes so a stuck attempt cannot lock a branch forever. Generate invalidates prior Open; does not overwrite live Reserved.

## Endpoints

| Surface | Path                                         | OpenAPI / SDK admin |
| ------- | -------------------------------------------- | ------------------- |
| Cloud   | `POST /cloud/branch-activations`             | included (generate) |
| Cloud   | challenges / exchange / resume / confirm     | excluded (machine)  |
| Branch  | `/branch/activation`, `/finalize`, `/status` | excluded            |

Confirm uses Branch installation token + receipt — **not** the human code after Reserved.  
SDK: only generate in OpenAPI/`schema.d.ts`; `BinexusClient.createBranchActivation(branchId)` for Web Admin.

## Sequence (confirm before local Active)

```text
generate code
→ Branch: material + session (no code stored)
→ challenge + signed exchange
→ Cloud Reserved / Activating + Receipt A
→ confirm Cloud → Active / Consumed
→ Branch TX local Active + Publish
→ permanent credentials; clear session
```

`POST /cloud/branch-activations/{activationId}/resume` requires a fresh challenge proof and the same InstanceId, public-key fingerprint, and token hash. It issues Receipt B and replaces the stored receipt hash. A stale receipt fails confirm recoverably; the installation token remains valid. Human code alone never resumes an attempt.

## Credential store

| Environment               | Provider                                                                                                                         |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Testing                   | InMemory                                                                                                                         |
| Development               | DevelopmentFile (`%LocalAppData%/binexus/branch-credentials` on Windows; the platform LocalApplicationData equivalent elsewhere) |
| Production/Staging Branch | ValidateOnStart **fails** until secure provider exists                                                                           |

The development files contain no activation code. Atomic replacement preserves the prior record on a cancelled or failed write. Cloud does not validate Branch credential-store options.

**Production blocker (required):**

```text
Branch Production deployment is blocked until a production credential-store provider exists.
```

## Health

Ready: `{ status, branchInstanceId }`  
Active (after **local** commit): `{ status, branchInstanceId, tenantId, branchId }`  
No `cloudConfirmation` field. Attempt stage via `GET /branch/activation/status` only.

## Runtime isolation

Cloud maps cloud endpoints; Branch maps branch endpoints. Cross-runtime → 404. Integration covered.

## Migration / SQL

- `20260715130453_Platform_BranchActivation`
- `apps/backend/db/binexus-idempotent.sql` regenerated
- `has-pending-model-changes`: none

## Tests (Release)

| Suite                               | Passed                        |
| ----------------------------------- | ----------------------------- |
| Unit                                | 98                            |
| Architecture                        | 41                            |
| Workers.Tests                       | 6                             |
| Integration                         | 176                           |
| Failed / skipped                    | 0 / 0                         |
| `@binexus/sdk` test/typecheck       | green                         |
| `@binexus/web` typecheck/lint/build | green                         |
| Cloud compose smoke                 | PASS (retry after free ports) |

## Verification

| Check                          | Result                                                                                                                           |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------- |
| Restore/build Release          | green (0 warnings) with `ASPNETCORE_ENVIRONMENT=Development` for OpenAPI host                                                    |
| NuGet vulnerable High/Critical | 0 / 0 (verify with package list)                                                                                                 |
| OpenAPI                        | generate included; machine Branch/cloud exchange/challenge/confirm excluded                                                      |
| SDK regen                      | included: `createBranchActivation(branchId)` is available in the handwritten client; generated schema follows `generate-sdk.ps1` |

## Risks

- Dual Principal after PG restore (Replace deferred).
- Dev file credential store is explicitly non-production.
- Active Cloud + local finalize failure recovered via `/branch/activation/finalize` using session.

## Out of scope

Pairing, DeviceId/TerminalId, Tauri, mDNS, LAN TLS, mTLS, sync, installer, Replace, Stripe entitlements, DPAPI provider.
