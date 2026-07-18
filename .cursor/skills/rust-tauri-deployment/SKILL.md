---
name: rust-tauri-deployment
description: Packaging, signing, updater, and release CI for Tauri v2 apps. Use when configuring tauri build, bundles, code signing, updater endpoints, or release workflows.
---

# rust-tauri-deployment

Follow [cicd-deployment.md](../rust-tauri-handbook/cicd-deployment.md).

## Always

- Sign Windows/macOS artifacts for distribution
- Updater: public key in config, private key only in CI secrets
- `createUpdaterArtifacts: true` when using updater
- Smoke-test installers on each supported OS
- Pin toolchain via `rust-toolchain.toml`

## Never

- Commit signing private keys
- Use `dangerousInsecureTransport` in production
- Ship with debug capabilities / wide permissions “temporarily”

## Matrix

Build on `windows-latest`, `macos-latest`, `ubuntu-latest` as required by product support. Binexus Branch Client prioritizes Windows first.
