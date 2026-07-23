# CHECKPOINT — BRANCH DEVICE AUTHENTICATION IMPLEMENTATION

**Status:** Implementation complete on working tree (not committed / not pushed)
**Base:** `origin/main` @ `2c95236`
**Branch:** `feat/branch-device-auth`
**Parent:** [branch-operational-security.md](../architecture/branch-operational-security.md)
**PLAN:** [PLAN.md](./PLAN.md) (approved with D1–D4 + contract corrections)

---

## Branch and base

| Item     | Value                                             |
| -------- | ------------------------------------------------- |
| Branch   | `feat/branch-device-auth`                         |
| Tracking | from `origin/main`                                |
| Commits  | **none** (no auto commit/push/PR per instruction) |

---

## Diff classified (working tree)

### Docs

- `docs/branch-device-auth/PLAN.md` — approved PLAN (D1–D4 + corrections)
- `docs/architecture/branch-operational-security.md` — accepted parent baseline
- `docs/architecture/branch-runtime.md`, `docs/README.md` — links
- This checkpoint

### Backend — contracts / crypto

- `Branching/DeviceAuth/*` — codec, formats, contracts, issuer/validator, status cache, service, auth scheme/policies
- Unit: `DeviceAuthCanonicalCodecTests`

### Backend — persistence

- `DeviceAuthChallenge` + EF config
- `BranchDevice.SecurityStamp` (bump on activate/revoke)
- Migration `20260718093009_Platform_BranchDeviceAuth` (+ UUID backfill for existing stamps)
- `appsettings.Testing.json` — BranchDeviceAuth test keys

### Backend — auth composition

- Scheme `DeviceAccessToken` + policies `BranchDeviceAndUser` / `BranchDeviceOnly`
- `RequireOperationalAuthorization` on Sales, Inventory, Orders, Warehouse, Logistics
- Endpoints: `POST /challenges`, `POST /tokens`, `GET /me`
- Rate limit `branch-device-auth`
- Boot validator + insecure-transport warning
- **Eager cache eviction on device revoke**

### Desktop — Phase 4 DAT lifecycle

- Rust `encode_device_auth_challenge` + version/audience constants
- `device_auth::DeviceAuthSession` — RAM-only DAT, single-flight PoP renew, public states
- `BranchClient` device-auth HTTP helpers
- IPC: `get_device_session_state`, `ensure_device_session`, `clear_device_session` (never returns DAT)
- Clear DAT on `retire_device`

### Tests

- Integration: `DeviceAuthEndToEndTests` (issue→me→revoke, replay, parallel redeem, anti-enumeration, Cloud regression, Branch Dev+User)
- Rust: device_auth public-state / IPC hygiene unit tests (39 lib tests green)

---

## HTTP / OpenAPI

| Method | Path                             | Auth                                                                          |
| ------ | -------------------------------- | ----------------------------------------------------------------------------- |
| POST   | `/branch/device-auth/challenges` | Anon + rate limit; Active-only mint; generic errors                           |
| POST   | `/branch/device-auth/tokens`     | Anon + PoP; body `{ challengeId, deviceId, signature, protocolVersion }` only |
| GET    | `/branch/device-auth/me`         | Device (DAT) only                                                             |

Header: `X-Binexus-Device-Authorization: Bearer <DAT>`
No `/refresh`. No client `credentialHash` on the wire.
OpenAPI group: `branch-v1`.

---

## Migration

`Platform_BranchDeviceAuth`:

- `branch_devices.security_stamp` (required; backfill via `gen_random_uuid()` for existing rows)
- `device_auth_challenges` (Open/Consumed, nonce, expiry, xmin)

---

## Verification run (this session)

| Gate                            | Result                                             |
| ------------------------------- | -------------------------------------------------- |
| `cargo test --lib` (desktop)    | 39 passed                                          |
| `DeviceAuthCanonicalCodecTests` | passed                                             |
| `DeviceAuthEndToEndTests` (7)   | passed after execution-strategy + GetMe claims fix |
| Platform build                  | passed                                             |

---

## Secret / key audit

| Material                       | Custody                                              |
| ------------------------------ | ---------------------------------------------------- |
| `BranchDeviceAuth.SigningKeys` | Branch Runtime only (HS256 v1 limitation documented) |
| `Jwt:SigningKey`               | Distinct; user JWT only                              |
| Pairing peppers / activation   | Distinct                                             |
| DAT                            | Rust RAM only; never IPC / disk / React              |
| `credentialHash`               | Server DB reconstruct for PoP; not on `/tokens` wire |

---

## Risks / blockers

1. **ADR-0023** still Proposed — no TLS/pinning; HTTP LAN is lab-only.
2. **Interim user JWT** — Dev+User needs integration-issued user tokens; no Desktop login UI (deferred to BRANCH USER SESSION).
3. **HMAC DAT** — any process with the key can mint; keys must not leave Branch.
4. **OpenAPI security schemes** for device-auth still minimal (Produces attributes present; richer contract docs optional follow-up).
5. **Product interop binary** for DAT (Rust→Kestrel→PG) not added as a separate exe; HTTP + codec covered by C# integration + Rust client methods.
6. No commits yet — human review before commit/PR.

---

## Proposed commits (when authorized)

1. `docs(branch-device-auth): approve PLAN and parent security baseline`
2. `feat(backend): device auth challenges, DAT issuer, security stamp`
3. `feat(backend): BranchDeviceAndUser on operational modules`
4. `feat(desktop): DAT lifecycle, single-flight, public session IPC`
5. `test(backend): device auth codec + end-to-end suite`
6. `docs(branch-device-auth): checkpoint implementation`

---

## Definition of Done checklist

```text
[x] D1–D4 recorded in PLAN
[x] /tokens body without client credentialHash
[x] Anti-enumeration challenges
[x] Atomic Open→Consumed consume
[x] BranchDeviceAuth boot validation
[x] HMAC limitation documented
[x] Desktop DAT-only scope (no login UI)
[x] Policies via composition, not domain if(Branch)
[x] Terminal server authority + stamp bump on revoke
[x] Cache key BranchInstanceId+DeviceId; eager eviction; fail-closed
[x] AllowInsecureBranchTransport explicit + warnings
[x] Phase 4 Desktop DAT lifecycle
[x] Integration revoke / replay / Cloud regression
[ ] Human review + commits + PR (blocked until you authorize)
```
