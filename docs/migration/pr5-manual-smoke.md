# Manual smoke — PR5 Branch Client pairing

Run on Windows MSVC with WebView2 and a local Branch Runtime (Kestrel + PostgreSQL).

## Environment record (PR5.2)

| Field                    | Value                                                                                 |
| ------------------------ | ------------------------------------------------------------------------------------- |
| Date (local)             | 2026-07-17 ~17:30–17:45 UTC-5                                                         |
| Base commit              | `e0d6edb` (`feat(backend): add device/terminal pairing backend (#80)`)                |
| Installer                | `Binexus_0.1.0_x64-setup.exe`                                                         |
| Final SHA-256            | `53B1C013729DCBD6B89EA2C3C07C5BFAC8006BE56C176F3094287F267EF0017E`                    |
| Size                     | 3 036 768 bytes                                                                       |
| Windows                  | Microsoft Windows NT 10.0.26200.0                                                     |
| Branch URL (sanitized)   | `http://127.0.0.1:5102`                                                               |
| BranchInstanceId (short) | `019f723e` (B/D host); earlier A/C/E/F/G/H host `019f7213`                            |
| Install path             | `%LOCALAPPDATA%\Binexus\binexus-desktop.exe`                                          |
| App id                   | `io.binexus.desktop`                                                                  |
| Host harness             | `apps/backend/spike/BranchGuiSmokeHost` (Branch mode + Testcontainers Postgres)       |
| WCM cleanup              | `cargo run --bin wcm_delete` / `Clear-DesktopProfile`                                 |
| CDP driver               | `%TEMP%\binexus-pw` + `apps/desktop/spikes/gui-smoke-cdp.mjs` (form fill/clicks only) |

Do not log pairing codes, status tokens, receipts, credentials, or private keys.

### Documented smoke hooks (spike host only)

| File under `%TEMP%`                     | Effect                                                                                                                                      |
| --------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `binexus-gui-smoke-discard-receipt.txt` | `IPairingReceiptVault.Discard(requestId)` — same effect as losing the one-shot vault after approve (forces receipt reissue on next confirm) |
| `binexus-gui-smoke-expire-request.txt`  | Marks request `Expired` in Postgres before approve (scenario D)                                                                             |
| `*.ack`                                 | Host writes `DISCARD_OK` / `EXPIRE_OK:Expired`                                                                                              |

## Prerequisites

1. Branch Server in Branch mode with pairing pepper; Active `BranchInstance`.
2. Admin JWT able to create sessions / approve / reject.
3. Clean profile: remove `%APPDATA%\io.binexus.desktop` and `%LOCALAPPDATA%\io.binexus.desktop`, then `wcm_delete`.
4. Install via NSIS `/S`. Prefer the NSIS artifact, not only `target/release`.

## Scenario matrix (PR5.2 + 5.3 re-smoke)

| Scenario                   | Result      | Observed UI                                                                       | Observed backend          | Persistence                      | Evidence                                              | Defect                                                        | Resolution                                        |
| -------------------------- | ----------- | --------------------------------------------------------------------------------- | ------------------------- | -------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------- |
| A Happy path               | PASS        | needsServerSetup → needsPairing → pendingApproval (fp) → paired; restart → paired | Active; approve → confirm | config + WCM                     | `%TEMP%\binexus-gui-smoke-A53.json`                   | UI casing; RecoveryRequired; IPC snake_case; fingerprint race | camelCase + boot flag + retire wipe + identity fp |
| A fingerprint              | PASS        | visible before approve; stable while polling; `5B6E-567E-910C`                    | admin short format        | identity PKCS#8                  | A53.json                                              | AppState fingerprint None                                     | resolve_ui_state + mergeAppUiState                |
| B Approve → kill → reissue | PASS        | PendingApproval (+fp) → Paired → restart Paired                                   | Active binding            | config Paired + WCM              | `%TEMP%\binexus-gui-smoke-B.json`                     | —                                                             | vault discard hook                                |
| C Reject before approve    | PASS        | Reject → needsPairing + reject notice; identity kept                              | reject 200                | temp cleared                     | `binexus-gui-smoke-rest.json`                         | Error left PendingApproval UI                                 | Clear transient pairing; `PAIRING_REJECTED`       |
| D Expire before approve    | PASS        | Expired notice; repair session OK                                                 | `EXPIRE_OK:Expired`       | temp cleared; device hash stable | `%TEMP%\binexus-gui-smoke-D.json`                     | —                                                             | Host expire hook; `PAIRING_EXPIRED`               |
| E Creds missing            | PASS        | `pairedCredentialsUnavailable`                                                    | —                         | config Paired, WCM deleted       | `binexus-gui-smoke-E.json`                            | —                                                             | —                                                 |
| F Single instance          | PASS        | first stays up                                                                    | —                         | config hash unchanged            | `%TEMP%\binexus-gui-smoke-F53.json` second_exit=**0** | panic/expect 101                                              | `try_acquire` + `exit(0)`                         |
| G URL validation           | PASS        | rejects public/metadata/query/fragment/path/userinfo; LAN OK                      | —                         | —                                | rest + unit test                                      | credentials-in-URL accepted                                   | Reject username/password in `url_policy`          |
| G redirect                 | PASS (code) | —                                                                                 | reqwest `Policy::none()`  | —                                | client.rs                                             | —                                                             | —                                                 |
| H Installer                | PASS        | NSIS `/S`; Start Menu `Binexus.lnk`; uninstall `/S`                               | —                         | WCM **not** cleared by uninstall | find-binexus-install.ps1                              | —                                                             | Documented; use `wcm_delete`                      |

### Scenario B evidence (required lines)

```text
receipt reissue path executed: yes
confirm result: success
binding active: yes
```

Vault discard after approve-while-dead forces status without raw receipt; `finalize` takes the reissue branch.

## Happy path steps (operator)

1. Install NSIS → open app.
2. Boot → Connect to Branch Server (`http://127.0.0.1:<port>`).
3. Admin: `POST /branch/pairing/sessions` → paste `{sessionId}:{code}` + terminal name.
4. Confirm code cleared from form; fingerprint when progress event arrives.
5. Admin approve → Finalizing/Paired.
6. Restart → Paired direct.

## Negative / recovery

| Scenario                                                            | Expected                                               |
| ------------------------------------------------------------------- | ------------------------------------------------------ |
| Public IP / 169.254.169.254 / credentials / query / fragment / path | URL rejected                                           |
| Second process                                                      | Exits (lock); first continues                          |
| config.json missing + WCM envelope                                  | RecoveryRequired                                       |
| Paired config + delete WCM (`wcm_delete`)                           | PairedCredentialsUnavailable; no auto identity         |
| Reject / expire before approve                                      | UI notice; temp cleared; same DeviceId; new session OK |

## Secrets check

- IPC / UI must not show private keys, receipts, or status tokens.
- `config.json` must not contain credentials.
- Uninstall does **not** delete WCM; clear with `wcm_delete` for clean reinstall tests.
