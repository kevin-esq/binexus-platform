# CHECKPOINT PR 5.1 — TEST, INTEROP, AUDIT AND BUNDLE GATES

**Date:** 2026-07-17  
**Branch:** `feat/desktop-tauri-pairing-client`  
**Base:** `e0d6edb`  
**Working tree:** uncommitted (no `git add` / `commit` / `push` / PR)

---

## Verdict

Gates 1–4 remain green. Gate 5 GUI smoke accepted in 5.2; final GUI defects (single-instance panic, fingerprint race) closed in **CHECKPOINT PR 5.3** (`docs/migration/pr5-checkpoint-5.3-final-gui-defects.md`).

---

## 1. Root cause of `STATUS_ENTRYPOINT_NOT_FOUND`

| Item                            | Finding                                                                                                                                  |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| Exit code                       | `0xC0000139` (`STATUS_ENTRYPOINT_NOT_FOUND`)                                                                                             |
| When                            | `cargo test --lib` / lib test harness exe load, before any `#[test]` runs                                                                |
| Missing export                  | ComCtl32 **v6** symbols (e.g. `TaskDialogIndirect`) imported by wry/tao                                                                  |
| DLL                             | `comctl32.dll` (test exe dependents include `comctl32.dll`)                                                                              |
| Loader binding                  | Without a v6 SxS manifest, Windows binds System32 ComCtl32 **v5.82**, which lacks those exports                                          |
| Why app bin works               | `tauri-build` embeds Common Controls v6 only via `rustc-link-arg-bins` for main binaries                                                 |
| References                      | tauri-apps/tauri#13419, #14580; community fixes (glyph PR#240, psysonic PR#1257)                                                         |
| WebView2                        | Present on host (150.x); not the failing import — failure is ComCtl32 entrypoint at process load                                         |
| `cargo test` vs `--lib` vs bins | Same issue for any artifact that links Tauri/wry **without** the v6 manifest (lib tests). Pure bins without Tauri symbols are unaffected |
| Core-crate split                | **Not required** for this failure; ComCtl32 fix unblocks `cargo test` on the product crate                                               |

`dumpbin /dependents` on the lib-test exe lists `comctl32.dll`. After the fix, `cargo test --lib` loads and runs.

---

## 2. Correction applied

```text
apps/desktop/src-tauri/build.rs
  → cargo:rustc-link-arg=/MANIFESTDEPENDENCY:…Microsoft.Windows.Common-Controls 6.0.0.0…
apps/desktop/src-tauri/windows-comctl-v6.manifest
  → same dependency documented for reviewers
```

Applies to all link units on `windows-msvc`. Duplicate on the app binary is merged by the linker with Tauri’s existing manifest.

---

## 3. `cargo test --workspace --all-targets` → PASS

```text
command: cargo test --workspace --all-targets
host: Windows MSVC (vcvars64) + rustc 1.97.1
result: PASS
lib tests: 23 passed, 0 failed, 0 ignored
exit code: 0
```

Also: `cargo clippy --all-targets -- -D warnings` → PASS.

---

## 4. `logic_smoke` classification

Kept as **additional smoke only** (`src/bin/logic_smoke.rs`). Comment updated: not a substitute for `cargo test`. CI no longer treats it as the primary Rust suite.

---

## 5. Interop Branch Runtime C# ↔ Rust (full protocol)

| Field        | Value                                                                                                                                                                 |
| ------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| workflow     | `.github/workflows/desktop.yml`                                                                                                                                       |
| job          | `desktop-windows` (`Desktop Windows MSVC`)                                                                                                                            |
| command      | `dotnet test apps/backend/tests/Binexus.IntegrationTests/Binexus.IntegrationTests.csproj -c Release --filter FullyQualifiedName~DevicePairingRustProductInteropTests` |
| harness      | `cargo build --bin pairing_interop` → product `BranchClient` + `PairingCeremony`                                                                                      |
| test names   | `Rust_product_client_completes_full_ceremony_against_branch_runtime_and_postgres`                                                                                     |
|              | `Rust_product_client_confirm_after_restart_uses_receipt_reissue_path`                                                                                                 |
| stack        | Rust product client → Branch Runtime (Kestrel TCP) → PostgreSQL (Testcontainers)                                                                                      |
| local result | **PASS** 2/2                                                                                                                                                          |
| duration     | ~6 s test body after build; first full run ~34 s including container                                                                                                  |
| exit code    | **0**                                                                                                                                                                 |

Ceremony covered: session+code → challenge → signed exchange → admin approve → status → confirm (normal or forced receipt reissue) → Paired → resume-paired restart simulation.

Not accepted as substitutes: wiremock-only, golden-vector-only, C#-only SimulatedPairingClient.

---

## 6. Exact CI job wiring

```text
workflow: Desktop (Tauri)  (.github/workflows/desktop.yml)
job: Desktop Windows MSVC
triggers: pull_request + push to main (path-filtered) + workflow_dispatch
required: must be added to branch protection (file alone is not enough — see §9)
```

Local interop evidence (this session):

```text
workflow: (local)
job: DevicePairingRustProductInteropTests
command: dotnet test … --filter FullyQualifiedName~DevicePairingRustProductInteropTests
test name: Rust_product_client_completes_full_ceremony_against_branch_runtime_and_postgres
result: PASS
duration: ~3 s (first) / suite 6 s
exit code: 0

test name: Rust_product_client_confirm_after_restart_uses_receipt_reissue_path
result: PASS
duration: ~0.7–1 s
exit code: 0
```

---

## 7. Advisories and individualized exceptions

Source of truth: `docs/migration/cargo-audit-exceptions-desktop.json` + `apps/desktop/src-tauri/audit.toml`.  
Lockfile audited: `apps/desktop/src-tauri/Cargo.lock` (same as build).

Attempted `cargo update -p quick-xml --precise 0.41.0` → **fails** (`plist 1.8.0` requires `quick-xml ^0.38.0`).

### RUSTSEC-2026-0194

| Field                | Value                                                                               |
| -------------------- | ----------------------------------------------------------------------------------- |
| Advisory ID          | RUSTSEC-2026-0194                                                                   |
| Severity             | High (7.5)                                                                          |
| Affected crate       | quick-xml **0.38.4**                                                                |
| Resolved version     | 0.38.4                                                                              |
| Patched              | >=0.41.0                                                                            |
| Path                 | binexus-desktop → tauri 2.11.5 → tauri-utils 2.9.3 → plist 1.8.0 → quick-xml 0.38.4 |
| Feature              | tauri default → compression/config; plist default                                   |
| Runtime reachability | plist XML attribute iteration; **not** Branch pairing JSON / WCM                    |
| Binexus impact       | CPU DoS only if untrusted plist/XML is parsed                                       |
| Why not update       | plist pin blocks 0.41 without Tauri matrix bump                                     |
| Mitigation           | No untrusted XML on pairing surface; URL policy                                     |
| Owner                | desktop-platform                                                                    |
| reviewBy             | 2026-10-17                                                                          |
| removalCondition     | Tauri matrix whose plist pulls quick-xml >=0.41.0                                   |

### RUSTSEC-2026-0195

| Field                                   | Value                              |
| --------------------------------------- | ---------------------------------- |
| Advisory ID                             | RUSTSEC-2026-0195                  |
| Severity                                | High (7.5)                         |
| Affected crate                          | quick-xml **0.38.4**               |
| Path / features                         | same as 0194 (NsReader via plist)  |
| Reachability                            | Untrusted namespaced XML via plist |
| Mitigation / owner / reviewBy / removal | same pattern as 0194               |

### RUSTSEC-2026-0009

| Field            | Value                                                              |
| ---------------- | ------------------------------------------------------------------ |
| Advisory ID      | RUSTSEC-2026-0009                                                  |
| Severity         | Medium (6.8)                                                       |
| Affected crate   | time **0.3.44**                                                    |
| Patched          | >=0.3.47                                                           |
| Path             | tauri/wry → cookie → time; also plist → time                       |
| Reachability     | Crafted Set-Cookie in WebView; pairing `reqwest` has no cookie jar |
| Mitigation       | Private/loopback URL policy + CSP                                  |
| Owner            | desktop-platform                                                   |
| reviewBy         | 2026-10-17                                                         |
| removalCondition | Verified time >=0.3.47 under Tauri 2.11.5 or matrix bump           |

Full `cargo audit` + `cargo tree -i` / `-e features` captured in this session (see agent transcript). No High advisory claimed “unreachable” without path/feature evidence.

---

## 8. Build Tauri real

```text
script: pnpm --filter @binexus/desktop build
  → package.json "build" = "pnpm build:vite && tauri build"
  → build:vite = tsc -b && vite build
  → tauri build = cargo build --release + NSIS bundler
```

```text
exit code: 0
duration: ~358 s
```

---

## 9. Artifact exact path and SHA-256

| Field          | Value                                                                           |
| -------------- | ------------------------------------------------------------------------------- |
| artifact path  | `apps/desktop/src-tauri/target/release/bundle/nsis/Binexus_0.1.0_x64-setup.exe` |
| filename       | `Binexus_0.1.0_x64-setup.exe`                                                   |
| size           | 3 029 012 bytes                                                                 |
| bundle type    | **NSIS** (also unpackaged `binexus-desktop.exe` 11 790 848 bytes)               |
| unsigned       | yes (no signing key)                                                            |
| SHA-256 (NSIS) | `C4096C83622922CDEB51DDE7913726F20C109E1399E7068A9B8E7259F8FF9B7B`              |
| SHA-256 (exe)  | `EE1AAAA1D596B5160E2DDDA7E05575D14265DE189DA1A16DB1825A9632CE5B95`              |

No `biexus-desktop.exe` typo in scripts/docs checked.

---

## 10–11. Smoke / receipt reissue

### Automated product-client smoke (required)

Covered by §5–6 including forced reissue path (`BINEXUS_MODE=reissue` → `confirm_forcing_reissue`).

### Interactive GUI smoke vs Branch Runtime

**Status: NOT COMPLETE in this session** (remaining blocker).

Executed partially:

| Step                                     | Evidence                                                         |
| ---------------------------------------- | ---------------------------------------------------------------- |
| App launch (release exe)                 | Starts (`first_running=True`)                                    |
| Second instance                          | Exits (`second_exit=101` — lock held; setup panics via `expect`) |
| URL policy / single-instance unit        | `cargo test` green                                               |
| Recovery / credentials missing UI states | unit tests on `resolve_ui_state`                                 |

Not executed interactively against live Branch UI: fresh profile wizard, fingerprint display, admin approve in Cloud UI, reject/expire before approval, public URL in UI, redirect rejection in UI.

`docs/migration/pr5-manual-smoke.md` remains the operator checklist.

---

## 12. State matrix (orchestrator / resolve)

| State (requested)            | Persist                                     | Entry                               | Exit                         | Secret cleanup               | Config update                           | Poller       | Test                                                             |
| ---------------------------- | ------------------------------------------- | ----------------------------------- | ---------------------------- | ---------------------------- | --------------------------------------- | ------------ | ---------------------------------------------------------------- |
| IdentityReady                | envelope created; config deviceId           | `initialize_device` no envelope     | NeedsServerSetup             | n/a                          | deviceId set                            | idle         | `identity_ready_maps_to_needs_server_setup`                      |
| ServerConfigured             | config status + URL                         | `configure_branch_url`              | NeedsPairing                 | keep envelope                | status ServerConfigured                 | idle         | `server_configured_maps_to_needs_pairing`                        |
| PendingApproval              | envelope pairing + config PairingInProgress | `exchange`                          | Approved/Rejected/Expired    | tokens in envelope           | pairing_request_id                      | poll status  | `pairing_in_progress_with_request_is_pending_approval` + interop |
| ApprovedPendingConfirm       | same + Approved on server                   | poll sees Approved                  | Paired / error               | receipt transient            | Finalizing UI event                     | finalize     | interop + orchestrator resume                                    |
| Paired                       | config Paired; pairing fields cleared       | confirm success                     | retire / recovery            | status_token/receipt cleared | branch/terminal ids                     | cancel       | `paired_with_envelope_is_paired` + interop resume                |
| RejectedBeforeApproval       | request Rejected                            | admin reject                        | error UI                     | keep identity                | may stay PairingInProgress until cancel | stop         | C# E2E + poller Rejected branch                                  |
| ExpiredBeforeApproval        | request Expired                             | TTL                                 | error UI                     | keep identity                | cancel/retire                           | stop         | poller Expired branch                                            |
| RejectedAfterApproval        | n/a product path                            | —                                   | —                            | —                            | —                                       | —            | server-side; client treats as PAIRING_FAILED                     |
| ExpiredAfterApproval         | challenge/receipt TTL                       | finalize fail                       | error / reissue              | —                            | —                                       | reissue path | interop reissue                                                  |
| RecoveryRequired             | config and/or reconcile                     | envelope w/o URL; DeviceId mismatch | assisted                     | never auto-regen             | RecoveryRequired                        | idle         | `envelope_without_config_is_recovery_required`, reconcile tests  |
| PairedCredentialsUnavailable | config Paired, no envelope                  | boot reconcile                      | assisted                     | —                            | status flip                             | idle         | `paired_without_envelope_is_credentials_unavailable`             |
| IdentityMismatch             | DeviceId config≠envelope                    | reconcile                           | RecoveryRequired             | —                            | RecoveryRequired                        | idle         | `reconcile_mismatch_device_id_requires_recovery`                 |
| SecretEnvelopeCorrupt        | WCM parse fail                              | get error                           | SecureStore error / recovery | —                            | —                                       | —            | Keyring get maps SecretStore (manual/WCM)                        |
| ConfigCorrupt                | JSON parse fail                             | load error                          | Configuration                | —                            | —                                       | —            | ConfigStore load Err                                             |

---

## 13. Persistence / reconciliation

Commit order (documented in `PairingCeremony`):

1. **Secure envelope** (WCM / harness file)
2. **Non-secret config.json** (tmp → bak → rename + `config.lock`)

Not a distributed transaction. `reconcile_partial_write` after crash:

- envelope present + no URL → RecoveryRequired
- Paired + no envelope → PairedCredentialsUnavailable
- DeviceId mismatch → RecoveryRequired
- never auto-regenerate identity

Tests: config atomic write / bak / two writers; file envelope roundtrip; reconcile suite in `pairing::ceremony::tests`.

---

## 14. Single-instance

`fs2` exclusive lock on `{app_data}/binexus-desktop.lock`.  
Unit: `single_instance::tests::second_acquire_fails_while_first_holds`.  
Release smoke: second process exit 101 while first held.

---

## 15–16. OpenAPI

```text
DevicePairingOpenApiContractTests → PASS (1)
artifacts/openapi/binexus-branch-v1.json → modified (Branch)
artifacts/openapi/binexus-v1.json → restored to HEAD (Cloud clean for this checkpoint)
```

---

## 17. Tauri JS / Rust versions (verified)

| Component         | Requested             | Resolved                                                   |
| ----------------- | --------------------- | ---------------------------------------------------------- |
| `@tauri-apps/api` | `package.json` 2.11.1 | `pnpm-lock.yaml` `@tauri-apps/api@2.11.1`                  |
| `@tauri-apps/cli` | 2.11.4                | lock `@tauri-apps/cli@2.11.4`                              |
| tauri (Rust)      | Cargo.toml `=2.11.5`  | `cargo tree` / `cargo tauri info` **2.11.5**               |
| tauri-build       | `=2.6.3`              | **2.6.3**                                                  |
| Compatibility     |                       | Confirmed by `pnpm --filter @binexus/desktop build` exit 0 |

No invented lockstep of patch versions across npm/crates.

---

## 18. Why `desktop.yml` instead of only `ci.yml`

- Host: **windows-latest MSVC** (Rust+Tauri+NSIS+WebView2). `ci.yml` backend/frontend jobs are **ubuntu-latest**.
- Avoid bloating every PR with a 60–90 min Windows Tauri build when desktop paths unchanged (path filters).
- Keeps .NET Branch interop + cargo audit + NSIS upload colocated with desktop toolchain pin (1.97.1).
- **Action required:** mark `Desktop Windows MSVC` as a **required** check in branch protection; path filters must not exclude the PR.

Workflow checks: PR trigger, path filters, no MinGW, pinned toolchain, fails if zero lib tests, uploads NSIS/exe, cleans temp harness dirs on `always()`, runs Rust↔Branch↔Postgres interop.

---

## Working tree / diff classification

Uncommitted. Includes: desktop product + ComCtl fix + ceremony/interop + `desktop.yml` + audit exceptions JSON + Branch OpenAPI + backend OpenAPI helpers + golden vectors + spikes. Cloud `binexus-v1.json` clean at HEAD.

---

## Remaining blockers (do not commit yet)

1. **Interactive GUI smoke** against real Branch (full checklist in §10) not executed end-to-end.
2. **Branch protection** must require `Desktop Windows MSVC` (cannot verify from this machine alone).
3. Second-instance UX currently **panics** (`expect`) instead of a clean exit code — functional lock works; polish optional before merge.
4. High advisories remain excepted until Tauri matrix bump (documented with `reviewBy`).

When GUI smoke is done and required-check is confirmed, approve PR 5.1 → then commits 0–7.
