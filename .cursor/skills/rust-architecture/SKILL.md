---
name: rust-architecture
description: Rust workspace and desktop app architecture — modular monolith, feature crates, clean boundaries, CQRS-lite, offline-first, DDD adapted to Rust. Use when structuring crates, extracting modules, designing offline sync, or choosing architecture for Tauri/POS/enterprise desktop.
---

# rust-architecture

## Preferred shape

**Modular monolith + vertical feature slices** in a Cargo workspace. Tauri host = composition root. Domain crates do not depend on `tauri`.

See [handbook architecture](../rust-tauri-handbook/architecture.md) and [folder structures](../rust-tauri-handbook/folder-structures.md).

## Always

- Flat crates under `crates/` (or feature modules in one crate until split is earned)
- `[workspace.dependencies]` for shared versions
- Dependency direction: domain ← app ← infra ← host (host depends inward)
- Explicit ports/traits only where tests or swappable backends need them
- Offline writes: outbox + idempotent operation keys

## Never

- Circular crate dependencies
- `tauri` / WebView types inside domain crates
- Full Clean Architecture ceremony for every CRUD screen
- Treating local SQLite as source of truth for POS money/stock (Binexus)

## When to split a crate

Compile time pain, separate ownership, or a reusable library boundary — not “layers look nice”.

## Patterns

| Pattern        | Role                                                     |
| -------------- | -------------------------------------------------------- |
| Vertical slice | Feature owns command + service + storage adapter         |
| CQRS-lite      | Commands mutate with invariants; queries read simply     |
| Repository     | Hide SQLite behind trait for tests                       |
| Outbox         | Durable sync queue in same DB transaction as local write |

## Binexus

Thin Branch Client: server owns commits; Rust owns pairing, secrets, hardware, local cache. Follow `docs/architecture/desktop-tauri.md`.
