# Binexus Desktop (Tauri)

Status: **scaffold only in Phase 0**. The `src-tauri/` directory contains the minimal Tauri 2 configuration so the app builds, but no native plugins or features are wired yet.

## What it is

A thin Tauri 2 wrapper that loads `apps/web` (Next.js) in a native window. We chose Tauri because Phase 4+ will need direct hardware access (receipt printers, scales, barcode readers) that a PWA can't provide.

## What it is NOT (in Phase 0)

- Not auto-updated
- Not signed
- Not packaged for distribution
- Not wired to any backend differently than the web app

## Development

```bash
# First, run the web app in dev (it serves at http://localhost:3000)
pnpm --filter @binexus/web dev

# Then in another shell, install Rust toolchain (one-time) and:
cd apps/desktop
pnpm tauri dev
```

Tauri requires the Rust toolchain (`rustup`). See <https://tauri.app/start/prerequisites/>.

## Why a separate app and not just a PWA?

PWAs can't drive USB/serial peripherals reliably across OSes. POS retail, restaurant, and warehouse all need that. Tauri gives us:

- Local IPC bridge to Rust where we'll talk to printers/scales (Phase 5+).
- Smaller binary than Electron.
- Same web build as the dashboard.
