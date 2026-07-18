# PR5.0 — Secure Storage Spike

Gate A evidence for the Tauri desktop pairing client. No custom encryption, no secret files on disk.

## Decision summary

| Item                  | Result                                                                                                                   |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| **Primary provider**  | `keyring` 3.6.2 with `windows-native` → Windows Credential Manager (WCM)                                                 |
| **Fallback provider** | DPAPI (`CryptProtectData` / `ProtectedData`) for Windows-only blob adapter                                               |
| **Stronghold**        | **NO-GO for PR5** — comparison only; no live spike; adds vault complexity without WCM integration benefit on Windows POS |
| **Gate A**            | **GO**                                                                                                                   |

## Versions and toolchain

| Component               | Version                                                     |
| ----------------------- | ----------------------------------------------------------- |
| Rust (pinned)           | **1.97.1** (`stable-x86_64-pc-windows-msvc`)                |
| `keyring`               | 3.6.2 + feature `windows-native`                            |
| `windows` (DPAPI spike) | 0.61.1                                                      |
| C# spike                | .NET 10, `System.Security.Cryptography.ProtectedData` 9.0.0 |
| OS                      | Windows 10.0.26200                                          |
| MSVC                    | VS 2026 Community + Windows SDK 10.0.22621                  |

**Toolchain note:** Current stable at decision date (2026-07-17) is **1.97.1**, not the plan’s incorrect “1.87.0”. Pinned for reproducibility on `x86_64-pc-windows-msvc`. Tauri MSRV is **1.77.2** (verified via `cargo info tauri`).

## Secret envelope v1 (real payload)

Single JSON blob stored as one credential value (not split, not file-backed):

```json
{
  "schema_version": 1,
  "device_id": "<uuid>",
  "private_key_pkcs8_base64": "<base64 pkcs8>",
  "device_credential_base64url": "<base64url>",
  "pairing": {
    "request_id": "<uuid|null>",
    "status_token": "<string|null>",
    "receipt": "<string|null>"
  }
}
```

Measured sizes: **505–509 bytes** UTF-8 JSON (within WCM generic credential limits).

## Scenarios executed

### Rust harness — `apps/desktop/spikes/secure-storage-spike`

Command:

```powershell
cmd /c "call `"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat`" && cd apps\desktop\spikes && cargo run -p secure-storage-spike"
```

| Provider | Scenario                | Result | Detail           |
| -------- | ----------------------- | ------ | ---------------- |
| envelope | serialize_v1            | PASS   | bytes=509        |
| keyring  | create                  | PASS   | stored           |
| keyring  | read                    | PASS   | len=509          |
| keyring  | overwrite               | PASS   | len=512          |
| keyring  | delete                  | PASS   | deleted          |
| keyring  | missing_entry           | PASS   | `NoEntry`        |
| dpapi    | protect_unprotect       | PASS   | cipher_bytes=760 |
| keyring  | two_process_child_store | PASS   | child stored     |
| keyring  | two_process_parent_read | PASS   | len=509          |

**Cross-process fix:** `keyring` without `windows-native` failed parent read after child store (`No matching entry`). Enabling `windows-native` resolved cross-process WCM visibility.

### C# harness — `apps/backend/spike/SecureStorageSpike`

Direct WCM (`CredWrite` / `CredRead` / `CredDelete`) + DPAPI via `ProtectedData`:

```powershell
dotnet run --project apps/backend/spike/SecureStorageSpike/SecureStorageSpike.csproj -c Release
```

All scenarios PASS (create/read/overwrite/delete/missing, envelope ~505 bytes, DPAPI roundtrip).

### Not executed live

| Provider                     | Reason                                                                                                  |
| ---------------------------- | ------------------------------------------------------------------------------------------------------- |
| Tauri Stronghold             | Desktop vault plugin; no Windows WCM integration; operational cost vs. native WCM; documented as NO-GO  |
| Process restart              | Manual: WCM persists across process exit (delete/recreate cycle proves persistence semantics)           |
| Reinstall / new Windows user | Credential is `CurrentUser` scoped; survives app restart, not profile migration (documented risk)       |
| Corrupt backend              | WCM returns read errors; spike treats as hard failure (no silent recovery)                              |
| CI headless                  | WCM requires interactive user session on Windows agents; CI must use mock/in-memory adapter (see risks) |

## Objective comparison

| Criterion                   | keyring + WCM                | DPAPI adapter             | Stronghold      |
| --------------------------- | ---------------------------- | ------------------------- | --------------- |
| OS integration              | Native WCM UI/manageable     | Opaque blob file possible | App-local vault |
| Cross-process               | PASS (with `windows-native`) | N/A (app holds blob ref)  | App-internal    |
| Envelope ~500B              | PASS                         | PASS                      | Expected PASS   |
| Headless CI                 | Hard (needs mock)            | Easier (in-memory)        | Hard            |
| License                     | MIT/Apache (keyring)         | OS API                    | Apache-2.0      |
| Advisories (spike lockfile) | none on keyring path         | none                      | not installed   |

## Recommended architecture

1. **Primary:** `keyring` → WCM, service `io.binexus.desktop`, account = device-scoped id.
2. **Fallback:** DPAPI-protected blob only inside a dedicated adapter when WCM unavailable (never raw JSON on disk).
3. **Never:** custom AES, plaintext secret files, splitting envelope across files.

## Risks

- **CI headless:** WCM unavailable without a Windows logged-in user → use trait + in-memory mock in tests; WCM only in manual/QA jobs.
- **Profile reset:** WCM entries lost on Windows profile wipe → `RecoveryRequired` flow (no auto-reconstruct from envelope alone).
- **Two-process startup:** WCM supports concurrent readers; writers must serialize (single-instance gate).

## Evidence artifacts

- Rust spike: `apps/desktop/spikes/secure-storage-spike/`
- C# spike: `apps/backend/spike/SecureStorageSpike/`
- Exit code 0 on both harnesses (2026-07-17)
