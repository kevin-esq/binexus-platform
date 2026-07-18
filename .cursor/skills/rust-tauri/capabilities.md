# Capabilities

Files live in `src-tauri/capabilities/*.json` with `$schema` pointing at `../gen/schemas/desktop-schema.json`.

## Rules

- One capability per security posture / window group
- Explicitly list capabilities in `tauri.conf.json` for production clarity
- Prefer deny-by-default; add the smallest permission that unblocks the feature
- Platform-specific permissions use `"platforms": ["windows", ...]`

## Binexus baseline

`main-capability` grants core event/window/app/webview only — no shell, fs, http plugin, clipboard, or updater until a reviewed need exists.

## Remote access

Default: API only for bundled assets. Remote URL capabilities require ADR-level review.

## What capabilities do not protect

Malicious/insecure Rust, overly wide scopes, missing checks inside commands, WebView 0-days, supply-chain compromise. See [`rust-security`](../rust-security/SKILL.md).
