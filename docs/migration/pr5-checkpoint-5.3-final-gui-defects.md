# CHECKPOINT PR 5.3 — FINAL GUI DEFECTS CLOSED

**Date:** 2026-07-17 (UTC-5)  
**Branch:** `feat/desktop-tauri-pairing-client` (uncommitted)  
**Base:** `e0d6edb`  
**No commits / push / PR** (per instructions)

---

## Verdict

Blockers from PR 5.2 closed: single-instance exits without panic; fingerprint is identity-derived, present in `get_app_state`, and not erased by partial progress events. Smoke A / B / F re-run against the new NSIS artifact.

---

## 1–3. Second instance (root cause, fix, exit code)

**Root cause:** `single_instance::acquire` mapped every failure to `AlreadyRunning`, and `tauri::Builder::run(...).expect(...)` turned setup/lock failure into a panic (exit 101 + stack).

**Fix:**

- `try_acquire` returns `AlreadyRunning` only on lock contention (`WouldBlock` / Win32 32|33).
- Real create/open/IO failures return `Failed`.
- `InstanceLock` is managed by Tauri (drop unlocks); no `unwrap`/`expect`/`panic` on this path.
- Duplicate instance → `std::process::exit(EXIT_ALREADY_RUNNING)` where `EXIT_ALREADY_RUNNING = 0`.
- Lock I/O failure → `EXIT_LOCK_FAILED = 1`.
- `run()` no longer uses `.expect(...)`.

**Observed (smoke F):**

```text
second instance exit code: 0
stderr: WebView2 "Failed to unregister class Chrome_WidgetWin_0" only (no panic/STACK)
first instance remains responsive: yes
config unchanged: yes
secret envelope untouched: yes (second exits before AppContext)
restart after first closes: yes
evidence: %TEMP%\binexus-gui-smoke-F53.json
```

---

## 4–6. Fingerprint race (root cause, ownership, stability)

**Root cause:** `derive_from_config` always set `device_fingerprint_short: None` for `PendingApproval`. `begin_pairing` returned that snapshot; progress/`get_app_state` could replace UI state without a fingerprint. Poller events without `fingerprintShort` could clear it via full replace.

**Fix:**

- Short display `A1B2-C3D4-E5F6` derived from local PKCS#8 identity (`fingerprint_short_from_pkcs8`), same format as Branch admin.
- `resolve_ui_state` / `get_app_state` always attach identity fingerprint when envelope exists (NeedsServerSetup → NeedsPairing → PendingApproval → Finalizing → Paired; retained on RecoveryRequired).
- Frontend `mergeAppUiState` never lets a partial DTO erase an existing fingerprint.
- Poller `finalizing` emit includes identity fingerprint.

**Smoke A:**

```text
fingerprint visible before admin approval: yes
fingerprint stable during polling: yes
fingerprint matches backend/admin view: yes (5B6E-567E-910C)
restart Paired: yes
evidence: %TEMP%\binexus-gui-smoke-A53.json
```

---

## 7. Tests added

| Area                     | Coverage                                                                                                                        |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------- |
| Rust `single_instance`   | first lock; second AlreadyRunning; first continues; reopen after drop; FS Failed ≠ AlreadyRunning; crash/drop recovery          |
| Rust `crypto`            | short display format; PKCS#8 short stable                                                                                       |
| Rust `commands`          | PendingApproval includes fingerprint; ServerConfigured preserves; stable across status flip                                     |
| Vitest `mergeAppUiState` | begin includes fp; progress without fp does not erase; poll snapshot preserves; reject→needsPairing preserves; progress-only fp |

`cargo test --workspace --all-targets` → **36** lib tests PASS.

---

## 8. Suites

```text
pnpm --filter @binexus/desktop test → 8 passed
cargo test --workspace --all-targets → 36 passed
cargo clippy --workspace --all-targets -- -D warnings → PASS
DevicePairingRustProductInteropTests → 2/2 PASS
pnpm --filter @binexus/desktop build → PASS
pnpm exec tauri build --bundles nsis → PASS
```

---

## 9. Smoke A / B / F

| Scenario                                    | Result                                          |
| ------------------------------------------- | ----------------------------------------------- |
| A happy path + fingerprint + restart        | **PASS**                                        |
| B approve→kill→vault discard→reissue→Paired | **PASS** (`receipt reissue path executed: yes`) |
| F second instance                           | **PASS** (exit **0**)                           |

---

## 10–11. Installer

```text
filename: Binexus_0.1.0_x64-setup.exe
size: 3036768
SHA-256: 53B1C013729DCBD6B89EA2C3C07C5BFAC8006BE56C176F3094287F267EF0017E
```

Previous hash `0A14D60D…` is superseded.

---

## 12. Working tree

Uncommitted on `feat/desktop-tauri-pairing-client` @ `e0d6edb`. No `git add` / commit / push / PR.

---

## 13. Remaining blockers

1. Branch protection: mark `Desktop Windows MSVC` required **after** PR open (not claimed done here).
2. Optional: suppress or redirect WebView2 unregister stderr on second-instance exit (cosmetic; not a panic).

Ready for commit split approval when you green-light PR 5.3.
