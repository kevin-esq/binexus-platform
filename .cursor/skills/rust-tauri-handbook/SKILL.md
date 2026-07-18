---
name: rust-tauri-handbook
description: Master index for the Rust + Tauri v2 Engineering Handbook — architecture, libraries, folder structures, workflows, checklists, scaffolding, CI/CD, anti-patterns. Use when setting standards, scaffolding a Tauri app, choosing crates, reviewing Rust/Tauri architecture, or when the user asks for the Rust+Tauri handbook or engineering brain.
---

# Rust + Tauri Engineering Handbook

Permanent Cursor standard for production Rust + Tauri v2 desktop apps. Research-backed (official Rust/Tauri docs, Microsoft Rust guidelines, blessed.rs, RustSec, production Tauri patterns). Tuned for Binexus Branch Client (`apps/desktop`) and reusable for greenfield Tauri projects.

## How to use

| Need                                           | Open                                                                                      |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Day-to-day Tauri commands / IPC / capabilities | [`rust-tauri`](../rust-tauri/SKILL.md)                                                    |
| Workspace, layers, DDD, offline-first          | [`rust-architecture`](../rust-architecture/SKILL.md) + [architecture.md](architecture.md) |
| Trust boundaries, CSP, secrets, supply chain   | [`rust-security`](../rust-security/SKILL.md)                                              |
| Ownership, errors, async, crates idioms        | [`rust-fundamentals`](../rust-fundamentals/SKILL.md)                                      |
| Unit / integration / WebDriver / CI tests      | [`rust-tauri-testing`](../rust-tauri-testing/SKILL.md)                                    |
| Startup, IPC, binary size, LTO                 | [`rust-tauri-performance`](../rust-tauri-performance/SKILL.md)                            |
| Local SQLite, sync, outbox                     | [`rust-sqlite`](../rust-sqlite/SKILL.md)                                                  |
| Bundling, updater, signing                     | [`rust-tauri-deployment`](../rust-tauri-deployment/SKILL.md)                              |
| Desktop UX (native feel, tray, windows)        | [`desktop-ux`](../desktop-ux/SKILL.md)                                                    |
| PR review rubric                               | [`rust-code-review`](../rust-code-review/SKILL.md)                                        |

## Reference map

1. [architecture.md](architecture.md) — chosen architecture, folder structures by size, patterns
2. [libraries.md](libraries.md) — recommended crates with rationale
3. [folder-structures.md](folder-structures.md) — small → enterprise layouts
4. [workflows.md](workflows.md) — develop / review / release loops
5. [checklists.md](checklists.md) — security, performance, release, PR
6. [scaffolding.md](scaffolding.md) — project templates and bootstrap steps
7. [cicd-deployment.md](cicd-deployment.md) — CI gates, bundles, updater
8. [anti-patterns.md](anti-patterns.md) — forbidden patterns
9. [documentation.md](documentation.md) — rustdoc, ADRs, capability docs
10. [research-sources.md](research-sources.md) — primary sources used

## Non-negotiable principles

1. **WebView is untrusted.** Secrets, auth, crypto, hardware, and privileged I/O live in Rust.
2. **Deny by default.** Capabilities grant the minimum permissions per window.
3. **Typed public errors.** Frontend gets stable codes, not raw `Display` chains or stack traces.
4. **Workspace when 2+ crates.** Flat `crates/` (or `src-tauri` + extracted libs); `[workspace.dependencies]`.
5. **thiserror in libs, anyhow only at binary edges** (or skip anyhow and keep typed `AppError` end-to-end — Binexus preference).
6. **Async for heavy work.** Never block the UI thread; use `spawn_blocking` for sync SQLite/CPU.
7. **Commit `Cargo.lock`.** Pin critical versions; `cargo deny` / `cargo audit` in CI.
8. **Binexus Branch Client:** no Postgres credentials in Tauri; local cache is non-authoritative (see `docs/architecture/desktop-tauri.md`).

## Architecture verdict (summary)

| App class                        | Preferred shape                                                                                         |
| -------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Large Tauri / enterprise desktop | Modular monolith: feature crates + thin Tauri host                                                      |
| POS / branch client (Binexus)    | Thin client: UX in webview, host = hardware + secure store + pairing + local cache; server owns commits |
| Offline-first                    | Local SQLite in Rust + outbox/sync; optional CRDT only when multi-writer is required                    |
| Cross-platform desktop           | Tauri v2 plugins + per-platform capabilities; system WebView                                            |

Details: [architecture.md](architecture.md).
