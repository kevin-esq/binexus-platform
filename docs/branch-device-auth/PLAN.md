# PLAN — BRANCH DEVICE AUTHENTICATION

**Status:** Approved for implementation
**Approved:** 2026-07-18
**Parent model:** [BRANCH RUNTIME OPERATIONAL SECURITY MODEL](../architecture/branch-operational-security.md) (accepted baseline)
**Branch:** `feat/branch-device-auth` (from `origin/main`)
**Out of scope:** LAN TLS/pinning, Branch User Session, login UI, Offline Sales, Sales TerminalId migration, SyncCred, mTLS, per-request DPoP, POS UI

Parent model + this PLAN govern implementation. ADRs 0018 / 0020 / 0023 remain **Proposed**.

---

## Human decisions (resolved)

| ID     | Decision                                                                                                                                                    |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **D1** | Apply `BranchDeviceAndUser` to **all** Branch operational modules: Sales, Inventory, Orders, Warehouse, Logistics                                           |
| **D2** | **Omit** `POST /branch/device-auth/refresh`. Renewal = challenge → tokens with fresh PoP                                                                    |
| **D3** | **No** hard HTTPS reject in this initiative. HTTP only for Development, CI, and explicit lab. Hard HTTPS + pinning → **LAN TLS AND BRANCH SERVER IDENTITY** |
| **D4** | **Defer** per-request DPoP. v1 = bearer DAT (5 min) + security stamp + status cache + fail-closed revocation                                                |

---

## 0. Objective and non-goals

**Objective:** Branch issues a short-lived **Device Access Token (DAT)** after ECDSA PoP; Branch operational APIs require **DAT + interim user JWT** (parent model). Desktop obtains/holds/attaches DAT only.

**Non-goals:** ADR Accepted flips; LAN TLS enforcement; user login UI / user JWT storage on Desktop; Offline Sales; Sales TerminalId Guid migration; SyncCred; mTLS; DPoP.

---

## 1. Credentials and keys after pairing

### 1.1 Inventory (unchanged physically; DAT usage corrected)

| Material                                | Role in DAT                                                        |
| --------------------------------------- | ------------------------------------------------------------------ |
| DeviceId                                | Challenge subject; DAT `sub`                                       |
| ECDSA private key (client)              | **Only** signs PoP for `/tokens`                                   |
| ECDSA public key / fingerprint (Branch) | Verify PoP                                                         |
| Raw device credential (client)          | Stays in keyring; **not** sent; **not** required in `/tokens` body |
| Credential hash (Branch DB)             | Server injects into canonical PoP when verifying                   |
| BranchInstanceId / Terminal binding     | Server-side consistency                                            |
| Pairing receipt                         | Unused for DAT                                                     |

### 1.2 Dual-secret responsibilities

```text
ECDSA private key  → PoP signature for DAT issuance (and pairing)
Raw device credential → long-term secret; hash stored on Branch at pairing;
                        NOT a second factor on the wire for /tokens;
                        NOT a DAT signing key;
                        NEVER in requests, responses, IPC, or logs
```

**Proof for `/tokens`:**

```text
possession of ECDSA private key
+ Device Active
+ persisted public key / fingerprint
+ correct BranchInstance
+ persisted credential/security state (server-side)
```

### 1.3 `/tokens` request (normative)

```json
{
  "challengeId": "uuid",
  "deviceId": "uuid",
  "signature": "base64url-p1363",
  "protocolVersion": "binexus-device-auth-challenge-v1"
}
```

Server loads from PostgreSQL: `credentialHash`, `publicKey`, `publicKeyFingerprint`, `BranchInstanceId`, challenge `nonce` / expiry / status, `security_stamp`, Active Terminal — then **authoritatively reconstructs** the canonical payload and verifies the signature.

**Do not** accept client-supplied `credentialHash` or `publicKeyFingerprint` as trust inputs.

---

## 2. DAT issuing authority

```text
Issuer:     Branch Runtime
Algorithm:  HS256 (v1 internal) — ANY holder of the HMAC key can mint DAT
Key:        BranchDeviceAuth.SigningKeys only (never Jwt:SigningKey, peppers, activation, sync)
kid:        BranchDeviceAuth.CurrentKeyId
Desktop:    never receives signing keys
Cloud:      never receives DAT keys
Modules:    never receive DAT keys — only validated principals
```

**HMAC limitation (v1):** treat key custody as equivalent to “can issue device sessions.” Future asymmetric issuer must keep **public claims unchanged**.

### Boot config (validated; refuse to start Branch operational host if invalid)

```text
BranchDeviceAuth:
  CurrentKeyId: string
  SigningKeys: [{ KeyId, Key (min 32 bytes UTF-8 or raw) }, ...]
  TokenLifetimeSeconds: 300
  ClockSkewSeconds: 30
  StatusCacheSeconds: 15
  ChallengeTtlSeconds: 60
  AllowInsecureBranchTransport: bool (env-dependent defaults)
```

Fail boot when: weak/missing current key, unknown `CurrentKeyId`, duplicate KeyIds, empty map.

Never log key material.

---

## 3. Ceremony

| Method | Path                             | Auth                                 |
| ------ | -------------------------------- | ------------------------------------ |
| POST   | `/branch/device-auth/challenges` | Anon + rate limits; anti-enumeration |
| POST   | `/branch/device-auth/tokens`     | Anon + PoP body                      |
| GET    | `/branch/device-auth/me`         | Device (DAT) only                    |

**No `/refresh`.**

### Challenges anti-enumeration

- Do **not** mint Open challenges for missing / non-Active devices.
- Public errors are **generic** (`DEVICE_PROOF_INVALID` / `DEVICE_AUTH_REQUIRED` class) — no distinction of missing vs Revoked vs Pending vs fingerprint mismatch.
- Rate limit by IP and DeviceId + global limits; uniform timing where practical.
- Never log nonce raw; clean up unused challenges.

### `/tokens` validation + atomic consume

1. Load challenge; verify Open + not expired (DB clock).
2. Load Device Active + materials; reconstruct canonical bytes; verify ECDSA.
3. **Atomic consume:**

```sql
UPDATE device_auth_challenges
SET status = 'Consumed', consumed_at = ...
WHERE id = @id AND status = 'Open' AND expires_at > now()
-- require exactly 1 row affected
```

4. Resolve exactly one Active Terminal; issue DAT.

Parallel redeem of same challenge ⇒ one DAT max.

### Response `/tokens`

```json
{
  "accessToken": "...",
  "tokenType": "binexus-device-access",
  "expiresAtUtc": "...",
  "deviceId": "...",
  "terminalId": "...",
  "branchInstanceId": "..."
}
```

No `credentialHash`, no stamp secrets beyond what’s needed for client opacity (stamp may be omitted from response; lives in DAT claims only).

---

## 4. Canonical PoP payload

Codec: length-prefixed UTF-8 (`u16 BE` + bytes), bidirectional golden vectors C#↔Rust.

Version: `binexus-device-auth-challenge-v1`

| #   | Field                 | Source                           |
| --- | --------------------- | -------------------------------- |
| 1   | protocolVersion       | constant                         |
| 2   | challengeId           | challenge                        |
| 3   | nonce                 | challenge                        |
| 4   | deviceId              | challenge / device               |
| 5   | branchInstanceId      | live instance / challenge        |
| 6   | audience              | `binexus-branch-device-auth`     |
| 7   | credentialHash        | **Branch DB only** (reconstruct) |
| 8   | publicKeyFingerprint  | **Branch DB only** (reconstruct) |
| 9   | challengeExpiresAtUtc | challenge (`O` UTC)              |

Client signs the same field order using values it already knows locally (it can recompute fingerprint/hash from its keys for signing) — **server never trusts client-transmitted hash/fingerprint**; it rebuilds from DB. Client and server must produce identical bytes when materials match.

Signature: ECDSA P-256 SHA-256, base64url P1363 (pairing-compatible).

---

## 5. DAT format

JWT HS256, Branch-signed.

Claims: `iss`, `aud=binexus-branch-device`, `sub=DeviceId`, `jti`, `iat`, `nbf`, `exp`, `branch_instance_id`, `tenant_id`, `branch_id`, `terminal_id` (consistency only), `device_security_stamp`, `token_type=binexus-device-access`, `ver=1`.

Header: `X-Binexus-Device-Authorization: Bearer <DAT>`
User: `Authorization: Bearer <user_jwt>`

---

## 6. Revocation / cache

Strategy: stamp in DAT + TTL 300s + status cache 15s.

```text
cache key = BranchInstanceId + DeviceId
value = { status, securityStamp, terminalId, tenantId, branchId }
```

Revoke / terminal disable / rebind → bump stamp + **eager eviction**.

If PostgreSQL unavailable and no valid non-expired cache entry → **503** `DEVICE_STATUS_UNAVAILABLE`.
**No stale-while-error** for Sales/Inventory (or any Dev+User route).

---

## 7. Bearer DAT (v1)

RAM-only in Rust; no disk; no React/IPC token; redact logs; renew via PoP; invalidate on stamp. Production safety requires future TLS initiative.

---

## 8. Transport

`AllowInsecureBranchTransport`:

- Default **true** only in Development/Testing.
- Other environments require explicit opt-in.
- Structured warning at boot + first DAT issue.
- Health/diagnostics may show insecure posture **without secrets**.
- Docs: **HTTP LAN is not a supported production configuration.**
- Do **not** claim ADR-0023 satisfied until TLS + pinning exist.

---

## 9. Interim user JWT

Backend Dev+User tested with **integration-issued** Identity JWTs.

Desktop in this initiative: **DAT only** (obtain, memory, attach). **No** login UI, user refresh, user JWT storage, offline login, POS roles → **BRANCH USER SESSION**.

---

## 10. Route policy

Branch: Sales, Inventory, Orders, Warehouse, Logistics → `BranchDeviceAndUser`.
Cloud: unchanged User-only.

Difference via **host composition / route group metadata / policies / schemes** — **not** `if (RuntimeMode == Branch)` inside domain handlers.

Pairing machine / activation / health / auth login: unchanged classes per parent matrix.

---

## 11–19. Pipeline, Desktop, Terminal, errors, store, OpenAPI, observability, tests

As previously specified in the draft PLAN, with corrections above. Summary deltas:

- Terminal: server authority; DAT `terminal_id` consistency claim only; stamp bump on binding change.
- Challenges: PostgreSQL + atomic UPDATE.
- Errors: generic anti-enumeration; 503 for status unavailable.
- Tests: include revoke-with-unexpired-DAT; Cloud unchanged; Rust IPC hygiene; C#↔Rust vectors; product interop path.

---

## 20. Phases (authorized)

```text
Phase 1 — Contracts and golden vectors
Phase 2 — Backend challenge, DAT issuer/validator, security stamp
Phase 3 — Branch auth schemes and operational policies
Phase 4 — Desktop Rust DAT lifecycle
Phase 5 — Integration, interop, CI, checkpoint
```

### Definition of Done

- [ ] All § corrections implemented
- [ ] D1 policies on five modules in Branch only
- [ ] Atomic challenge consume + anti-enumeration
- [ ] Boot validation of `BranchDeviceAuth`
- [ ] Stamp revocation rejects live DAT
- [ ] Desktop DAT RAM-only, no user-session scope creep
- [ ] Cloud regressions green
- [ ] CHECKPOINT document
- [ ] No automatic commit/push/PR (human-driven)

---

## DECISIONS REQUIRING HUMAN APPROVAL

**None remaining** — D1–D4 and contract corrections accepted 2026-07-18.
