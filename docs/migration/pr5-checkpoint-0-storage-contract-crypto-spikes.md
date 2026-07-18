# CHECKPOINT PR 5.0 — STORAGE, CONTRACT AND CRYPTO SPIKES

**Date:** 2026-07-17  
**Scope:** Gate PR5.0 discovery only — no product wizard, no PairingOrchestrator, no commits/PR.  
**Branch:** `feat/desktop-tauri-pairing-client`  
**Base:** `e0d6edb` — `feat(backend): add device/terminal pairing backend (#80)`

---

## 1. Git branch / base / working tree

```
Branch: feat/desktop-tauri-pairing-client @ e0d6edb
Working tree (uncommitted):
  M  apps/backend/.../BranchDevicePairingEndpointExtensions.cs
  M  apps/backend/.../BranchHealthEndpointExtensions.cs
  M  apps/backend/.../RuntimeHealthEndpointExtensions.cs
  M  apps/backend/tests/.../DevicePairingOpenApiContractTests.cs
  M  artifacts/openapi/binexus-branch-v1.json
  ?? apps/backend/spike/SecureStorageSpike/
  ?? apps/backend/src/.../BranchDevicePairingOpenApiExtensions.cs
  ?? apps/backend/tests/.../DevicePairingGoldenVectorTests.cs
  ?? apps/backend/tests/.../DevicePairingGoldenVectorInteropTests.cs
  ?? apps/desktop/spikes/
  ?? apps/desktop/src-tauri/rust-toolchain.toml
  ?? docs/migration/pr5-secure-storage-spike.md
  ?? docs/migration/pr5-checkpoint-0-storage-contract-crypto-spikes.md

Cloud OpenAPI: artifacts/openapi/binexus-v1.json — NO DIFF (restored after accidental regen)
```

---

## 2. Versions really verified

| Layer             | Pinned / verified                                                     |
| ----------------- | --------------------------------------------------------------------- |
| Rust toolchain    | **1.97.1** stable (`apps/desktop/src-tauri/rust-toolchain.toml`)      |
| Tauri runtime     | **2.11.5** (MSRV **1.77.2**)                                          |
| tauri-build       | **2.6.3**                                                             |
| tauri-runtime-wry | **2.11.4**                                                            |
| keyring           | **3.6.2** + `windows-native`                                          |
| p256 / ecdsa      | **0.13.2**                                                            |
| Crypto (.NET)     | `EcdsaP256ActivationCrypto` — IEEE P1363, SHA-256, SPKI DER Base64Url |
| Backend           | .NET 10, existing pairing backend from PR4                            |

**Correction:** Plan text “Rust 1.87.0 stable” was wrong. Current stable at decision date: **1.97.1**.

---

## 3. Toolchain (corrected)

| Checkpoint field                | Value                                                                     |
| ------------------------------- | ------------------------------------------------------------------------- |
| Current stable at decision date | **1.97.1** (2026-07-14)                                                   |
| Selected pinned toolchain       | **1.97.1** in `apps/desktop/src-tauri/rust-toolchain.toml`                |
| Reason for pinning              | Reproducible Windows MSVC builds; satisfies Tauri MSRV 1.77.2 with margin |
| Tauri MSRV                      | **1.77.2**                                                                |
| Windows build result            | **PASS** — `x86_64-pc-windows-msvc` (requires `vcvars64.bat`)             |

### Commands run (spikes workspace + Tauri spike)

| Command                                                   | Result                                                        |
| --------------------------------------------------------- | ------------------------------------------------------------- |
| `cargo build` (spikes)                                    | PASS                                                          |
| `cargo clippy -- -D warnings` (spikes)                    | PASS                                                          |
| `cargo test` (spikes)                                     | N/A — bin-only spikes; validation via executable + .NET tests |
| `cargo build --release` (Tauri capabilities spike)        | PASS                                                          |
| `cargo tauri build` (Tauri 2.11.5 + tauri-cli **2.11.4**) | PASS — `tauri-capabilities-spike.exe`                         |

---

## 4. Secure store chosen

| Role         | Provider                                             |
| ------------ | ---------------------------------------------------- |
| **Primary**  | `keyring` 3.6.2 → Windows Credential Manager         |
| **Fallback** | DPAPI adapter (Windows-only encrypted blob)          |
| **Rejected** | Stronghold (NO-GO), custom crypto, disk secret files |

Detail: [pr5-secure-storage-spike.md](./pr5-secure-storage-spike.md)

---

## 5. Windows results

| Spike                       | Exit | Notes                                              |
| --------------------------- | ---- | -------------------------------------------------- |
| secure-storage-spike (Rust) | 0    | All 9 scenarios green                              |
| SecureStorageSpike (C# WCM) | 0    | All 7 scenarios green                              |
| crypto-interop-spike        | 0    | C# sigs verify in Rust; Rust roundtrip OK          |
| single-instance-spike       | 0    | Lock blocks 2nd process; recovers after child kill |
| tauri-capabilities-spike    | 0    | debug + release build                              |

---

## 6. CI / headless results

| Area                     | Status                                                    |
| ------------------------ | --------------------------------------------------------- |
| OpenAPI contract test    | PASS in IntegrationTests (Postgres fixture)               |
| Golden vector .NET tests | PASS (3 tests)                                            |
| WCM on headless agent    | **Not validated** — expect mock secure-store trait in CI  |
| Tauri GUI build on CI    | **Deferred** to commit 1 (needs WebView2 + tauri-cli job) |

---

## 7. Secret envelope final (v1)

Single WCM credential value, JSON:

- `schema_version: 1`
- `device_id`, `private_key_pkcs8_base64`, `device_credential_base64url`
- `pairing.{request_id, status_token, receipt}` (nullable fields)

Policy (PR5): **config missing + envelope present → `RecoveryRequired`** — no auto-`Paired` from local envelope alone; server validation required.

---

## 8. OpenAPI Branch complete and reproducible

**Gate B: GO**

- Source: backend route metadata (`BranchDevicePairingOpenApiExtensions`, health OpenAPI helpers) — **not manual JSON edits**
- Artifact: `artifacts/openapi/binexus-branch-v1.json`
- Contract test: `DevicePairingOpenApiContractTests` — **15 routes**, typed schemas, Problem Details on 400, no secret examples
- Regenerate: `$env:BINEXUS_UPDATE_OPENAPI=1; dotnet test ... --filter DevicePairingOpenApiContractTests`
- Reproducibility: two consecutive fetches identical

Routes covered for Rust client:

- `/health/runtime`, `/health/branch`
- Admin: terminals, sessions, requests, approve/reject, devices, revoke
- Machine: challenges, exchange, status, receipt challenges/reissue, confirm

---

## 9. Cloud OpenAPI unchanged

`artifacts/openapi/binexus-v1.json` — **zero diff** vs base (verified after restore).

---

## 10. Golden vectors C# ↔ Rust

**Gate C: GO**

Fixtures:

- `apps/desktop/spikes/fixtures/pairing-crypto-golden-v1.json` (C# export)
- `apps/desktop/spikes/fixtures/pairing-crypto-rust-signatures-v1.json` (Rust signatures)

Verified:

| Check                         | Status                                         |
| ----------------------------- | ---------------------------------------------- |
| Canonical payload bytes (hex) | Fixed test vectors                             |
| SPKI Base64Url public key     | MATCH (DER, not raw EC point)                  |
| SHA-256 fingerprint           | MATCH                                          |
| IEEE P1363 signatures         | MATCH format                                   |
| C# signature → Rust verify    | PASS                                           |
| Rust signature → C# verify    | PASS (`DevicePairingGoldenVectorInteropTests`) |
| Byte-identical re-sign        | **Not required** (ECDSA k non-deterministic)   |

Export: `$env:BINEXUS_EXPORT_GOLDEN_VECTORS=1; dotnet test ... --filter DevicePairingGoldenVectorTests`

---

## 11. Capabilities validated against real schemas

**Spike:** `apps/desktop/spikes/tauri-capabilities-spike/`

Tauri **2.11.5** + tauri-build **2.6.3** generated:

- `src-tauri/gen/schemas/desktop-schema.json`
- `src-tauri/gen/schemas/capabilities.json`

Validated capability `main-capability` (window `main` only):

```json
["core:event:default", "core:window:default", "core:app:default", "core:webview:default"]
```

Explicitly **absent:** shell, fs, http (JS), process, clipboard, updater plugins.

CSP (tauri.conf.json):

```
default-src 'self'; connect-src 'none'; img-src 'self' data:; script-src 'self'; style-src 'self' 'unsafe-inline'
```

HTTP to Branch: **Rust only** (reqwest in product commit 3+).

---

## 12. Single-instance decision

**Authority:** `fs2` exclusive file lock pre-init (spike proven).

| Topic                          | Decision                                                                              |
| ------------------------------ | ------------------------------------------------------------------------------------- |
| Authority                      | File lock at `%TEMP%/binexus-pr5-single-instance-spike.lock` (product: app-data path) |
| `tauri-plugin-single-instance` | **Secondary/deferred** — evaluate in commit 2; do not combine without ordering doc    |
| Second instance                | Blocked at lock acquire (OS error 33)                                                 |
| Orphan lock file               | Empty/orphan file recoverable; lock released on process exit                          |
| Crash                          | Lock released when handle closes; spike: acquire after child kill PASS                |
| Two-process test               | Real child process — PASS                                                             |

---

## 13. Dependencies and audits

### Rust (spikes `Cargo.lock`)

```
cargo audit → 1 warning: rand 0.8.5 RUSTSEC-2026-0097 (unsound w/ custom logger)
```

Mitigation for product: bump to `rand 0.9+` or isolate RNG usage; spike uses `rand 0.8.5` only for envelope sample bytes.

### .NET

Existing backend packages — no new NuGet in product path from spikes.

### Licenses (spike crates)

keyring (MIT/Apache), p256 (Apache-2.0/MIT), tauri (Apache-2.0/MIT), fs2 (MIT/Apache).

---

## 14. Risks and blockers

| Risk                               | Severity                                               | Mitigation                                                                                                      |
| ---------------------------------- | ------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------- |
| WCM unavailable in CI              | Medium                                                 | Secure-store trait + in-memory mock                                                                             |
| LAN URL SSRF                       | High (ops)                                             | Policy documented: literal private IP + explicit hostname resolution rules; no redirects; block 169.254.169.254 |
| No TLS in PR5                      | High                                                   | Documented — LAN policy reduces SSRF, does not replace TLS/pinning                                              |
| `cargo tauri build` not executed   | **Resolved** — PASS on capabilities spike (2026-07-17) |
| ECDSA non-deterministic signatures | Info                                                   | Cross-verify only, never compare signature bytes across impls                                                   |
| Envelope without config            | Medium                                                 | `RecoveryRequired` + server probe; no local-only Paired                                                         |

**Blockers for PR5 implementation:** None — **GO** pending your approval of this checkpoint.

---

## 15. Files implementation would touch (commits 1–7)

### Commit 1 — Tauri shell scaffold

- `apps/desktop/package.json`, `vite.config.ts`, `index.html`
- `apps/desktop/src-tauri/` (Cargo.toml, tauri.conf.json, capabilities, main.rs, lib.rs, build.rs)
- `apps/desktop/src-tauri/rust-toolchain.toml` _(already pinned)_
- `.github/workflows/desktop.yml` (stub)

### Commit 2 — Single instance + CSP hardening

- `apps/desktop/src-tauri/src/single_instance.rs`
- `apps/desktop/src-tauri/capabilities/main-capability.json`
- Integration test / spike port from `single-instance-spike`

### Commit 3 — Secure store + envelope

- `apps/desktop/src-tauri/src/secrets/` (trait, wcm, dpapi fallback)
- Envelope serde types mirroring v1 schema

### Commit 4 — Branch HTTP client (Rust)

- `apps/desktop/src-tauri/src/branch/` (reqwest, OpenAPI-generated or hand-typed from artifact)
- LAN URL validator module

### Commit 5 — Pairing crypto (Rust)

- Canonical codecs port from golden vectors
- p256 sign/verify

### Commit 6 — Pairing orchestrator + IPC

- `pairing/` module, Tauri commands (high-level only)
- React wizard shell (no POS)

### Commit 7 — Tests + docs

- E2E pairing against Branch test fixture
- ADR-0020 update

### Backend (already in working tree for Gate B)

- `BranchDevicePairingOpenApiExtensions.cs`
- Health OpenAPI helpers
- Contract + golden vector tests
- `artifacts/openapi/binexus-branch-v1.json`

---

## 16. Adjusted commit plan 1–7

| #   | Focus                                                                                  | Depends on checkpoint |
| --- | -------------------------------------------------------------------------------------- | --------------------- |
| 1   | Tauri 2.11.5 shell, React/Vite, toolchain, tauri-cli 2.11.4, `cargo tauri build` green | §3, §11               |
| 2   | Single-instance file lock authority; optional plugin evaluation doc                    | §12                   |
| 3   | WCM primary + DPAPI fallback; envelope v1                                              | §4, §7                |
| 4   | Branch HTTP client from OpenAPI artifact; LAN allowlist                                | §8, URL policy        |
| 5   | Crypto module from golden vectors                                                      | §10                   |
| 6   | Pairing wizard + orchestrator (RecoveryRequired policy)                                | §7                    |
| 7   | Integration tests, operator docs, ADR                                                  | all gates             |

**Explicitly out of PR5:** POS, sync, mDNS, TLS, device auth ops, installer, hardware, auto-update.

---

## Gate summary

| Gate                  | Result |
| --------------------- | ------ |
| A — Secure storage    | **GO** |
| B — OpenAPI Branch    | **GO** |
| C — Crypto interop    | **GO** |
| Capabilities schema   | **GO** |
| Single-instance spike | **GO** |

**Approved.** The pairing-client core implementation is in progress on `feat/desktop-tauri-pairing-client`. The crate uses the pinned Tauri, keyring, and crypto dependencies; Windows compilation still requires a Rust 1.97.1 toolchain to be available on the build host.
