---
name: rust-security
description: Security standards for Rust and Tauri v2 — trust boundaries, capabilities, CSP, secrets, unsafe, supply chain (cargo audit/deny), IPC hardening. Use when adding commands, plugins, filesystem/HTTP access, crypto, keyring, updater, or reviewing security of desktop Rust code.
---

# rust-security

## Trust boundary

| Side                | Trust            |
| ------------------- | ---------------- |
| WebView JS/HTML     | Untrusted        |
| Rust core + plugins | Fully privileged |

Assume XSS can call any command the window’s capabilities allow.

## Always

- Minimal capabilities per window
- CSP enabled and as tight as practical
- Secrets only in OS keyring / memory — never frontend or plaintext config committed
- Parse inputs into validated types before use
- Stable public error codes; no path/secret leakage
- `cargo audit` + `cargo deny` in CI; commit `Cargo.lock`
- Document every `unsafe` with a `// SAFETY:` invariant

## Never

- Broad `fs`/`shell` permissions for convenience
- `dangerousInsecureTransport` on updater in prod
- Log tokens, device keys, or pairing material
- Trust frontend for authorization decisions
- Ignore advisories without owner + expiry

## Tauri checklist

See [checklist.md](checklist.md) and handbook [checklists](../rust-tauri-handbook/checklists.md).

## Rust checklist

- No `unwrap` in security-sensitive paths
- Constant-time compare for secrets when relevant
- TLS via rustls; validate URLs against allowlists (Binexus `url_policy`)
- Review `build.rs` and new proc-macro deps

## Incident

1. Stop and notify user
2. Rotate exposed secrets (history counts)
3. Grep for the same pattern
4. Narrow capabilities / patch before other work

## References

- https://v2.tauri.app/security/
- https://v2.tauri.app/security/capabilities/
- https://v2.tauri.app/security/csp/
