# CHECKPOINT PR 5.2 — GUI SMOKE COMPLETE

**Date:** 2026-07-17 (UTC-5)  
**Branch:** `feat/desktop-tauri-pairing-client` (uncommitted)  
**Base:** `e0d6edb`  
**No commits / push / PR** (per instructions)

---

## Verdict

Interactive GUI smoke against NSIS-installed `binexus-desktop.exe`, real Branch Runtime (Kestrel) + PostgreSQL, CDP form fill/clicks (not DevTools invoke as the happy-path driver): **A–H closed**.

---

## 1. Scenario results A–H

| Scenario                                                             | Result                                                      |
| -------------------------------------------------------------------- | ----------------------------------------------------------- |
| A Happy path + restart Paired                                        | **PASS**                                                    |
| B Approve → kill → vault discard → reissue → Paired → restart Paired | **PASS**                                                    |
| C Reject before approve                                              | **PASS**                                                    |
| D Expire before approve                                              | **PASS**                                                    |
| E Creds missing                                                      | **PASS**                                                    |
| F Single instance                                                    | **PASS** (second exit 101)                                  |
| G URL / redirect                                                     | **PASS** (credentials URL fixed; redirect `Policy::none()`) |
| H Installer / uninstall                                              | **PASS**; WCM survives uninstall (documented)               |

---

## 2–3. Installer + SHA-256

```text
artifact: apps/desktop/src-tauri/target/release/bundle/nsis/Binexus_0.1.0_x64-setup.exe
filename: Binexus_0.1.0_x64-setup.exe
size: 3036181
SHA-256: 0A14D60DC49B33F24A4940CFA8C52FFF4B4C165479A94641851A2A245E7B14D9
unsigned: yes
install dir: %LOCALAPPDATA%\Binexus\
```

---

## 4. Restart after Paired

```text
result: PASS
ui: paired / "This terminal is ready"
evidence: %TEMP%\binexus-gui-smoke-A-restart.json; B secondRestart in binexus-gui-smoke-B.json
```

---

## 5. Receipt reissue (scenario B)

```text
kill before approve: yes
approve while dead: yes
vault discard ack: DISCARD_OK
UI after restart (before resume): PendingApproval (not Paired)
receipt reissue path executed: yes
confirm result: success
binding active: yes
second restart: Paired direct
evidence: %TEMP%\binexus-gui-smoke-B.json
```

Documented mechanism: spike host discards the one-shot `InMemoryPairingReceiptVault` entry (same effect as vault loss after approve). Product `finalize` then runs receipt challenge → reissue → confirm.

Interop remains green: `Rust_product_client_confirm_after_restart_uses_receipt_reissue_path`.

---

## 6–7. Rejected / Expired / PairedCredentialsUnavailable

| Case                         | Result                                                                                                            |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| Rejected before approval     | PASS — temp cleared; identity kept; UI notice                                                                     |
| Expired before approval      | PASS — `EXPIRE_OK:Expired`; UI “This pairing request expired…”; device hash `63445d54c6a2` stable; new session OK |
| PairedCredentialsUnavailable | PASS after `wcm_delete` + restart                                                                                 |

Evidence D: `%TEMP%\binexus-gui-smoke-D.json`.

---

## 8. Single-instance + crash recovery

```text
second instance exit: 101
first remained running: yes
orphan lock after close: no
```

---

## 9. URL / redirect rejection

Rejected: public IP, link-local metadata, query, fragment, unexpected path, userinfo.  
Accepted: `http://127.0.0.1:5102`.  
Redirects: `reqwest` `redirect::Policy::none()`.

---

## 10. Backend after flows

```text
Branch URL: http://127.0.0.1:5102
BranchInstanceId short (B/D host): 019f723e
health/branch status: Active
```

Pairing codes / tokens / receipts not recorded.

---

## 11. Defects found and fixes

| Defect                                                  | Fix                                                                                                   |
| ------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| UI blank: TS PascalCase `kind` vs Rust camelCase        | Align TS kinds; Vitest                                                                                |
| `envelope && !url` → RecoveryRequired on first run      | `config_file_missing_on_boot`; Uninitialized+envelope → NeedsServerSetup                              |
| Retire kept envelope → stuck RecoveryRequired           | Retire deletes WCM, resets config, mints identity                                                     |
| IPC snake_case fields                                   | `rename_all_fields = "camelCase"` on `AppUiState`                                                     |
| Fingerprint not applied on pending                      | Listen `pendingApproval` progress                                                                     |
| URL with `user:pass@` accepted                          | Reject username/password in `url_policy` + unit test                                                  |
| Reject/Expire left PendingApproval UI; temp not cleared | Orchestrator clears transient pairing; `PAIRING_REJECTED` / `PAIRING_EXPIRED` notices on NeedsPairing |

Smoke harness (not product): BranchGuiSmokeHost discard/expire file hooks; CDP/scripts under `apps/desktop/spikes/`.

---

## 12. Suites re-run after fixes

```text
cargo test --workspace --all-targets → exit 0 (25 lib tests)
pnpm --filter @binexus/desktop test → 3 passed
DevicePairingRustProductInteropTests → 2/2 PASS
tauri build --bundles nsis → exit 0 (SHA above)
logic_smoke → additional only (does not gate cargo test)
```

---

## 13–14. Working tree / diff class

Uncommitted on `feat/desktop-tauri-pairing-client`. Cloud OpenAPI `artifacts/openapi/binexus-v1.json` restored to HEAD.

Classification:

| Class | Areas                                                                 |
| ----- | --------------------------------------------------------------------- |
| feat  | desktop shell, pairing, WCM, wizard                                   |
| fix   | UI kinds, boot recovery, retire, URL credentials, reject/expire clear |
| test  | cargo unit, Vitest, Rust↔Branch interop, golden vectors               |
| spike | BranchGuiSmokeHost, gui-smoke-\*.ps1/mjs, storage/crypto spikes       |
| ci    | `.github/workflows/desktop.yml`                                       |
| docs  | PR5 checkpoints + manual smoke                                        |

---

## 15. Remaining blockers

1. Branch protection: add required check `Desktop Windows MSVC` **after** PR open (out of working tree). Do not mark done from local docs.
2. Second-instance still exits via panic (`expect`) rather than a clean exit code (optional polish).
3. Fingerprint timing on A remains PARTIAL (progress listener mitigates; poller can still win the race).

Commits 0–7: ready for your approval to split and create (no `git add` / `commit` / `push` / `gh pr create` until then).
