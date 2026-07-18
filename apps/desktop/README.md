# Binexus Branch Client (desktop)

Tauri 2 Branch Client for device pairing with a Branch Server. Not the Cloud operator panel (`apps/web`).

## Stack

| Component    | Version                           |
| ------------ | --------------------------------- |
| Rust         | 1.97.1 (`x86_64-pc-windows-msvc`) |
| Tauri        | 2.11.5                            |
| tauri-build  | 2.6.3                             |
| tauri-cli    | 2.11.4                            |
| Vite + React | Vite 6 / React 19                 |

## Develop

```powershell
# From repo root (requires MSVC + WebView2)
pnpm install
pnpm --filter @binexus/desktop dev

# Vite-only (CI / turbo). Full NSIS installer:
pnpm --filter @binexus/desktop build:app
```

## Pairing payload

PR5 machine APIs require both `pairingSessionId` and `pairingCode`. Paste:

```text
{pairingSessionId}:{8-digit-code}
```

## Security notes

- Secrets stay in Windows Credential Manager (`keyring` 3.6.2).
- Config is non-secret JSON under app data.
- JS has no network (`connect-src 'none'`); Branch HTTP is Rust-only.
- Single-instance via `fs2` file lock only.
