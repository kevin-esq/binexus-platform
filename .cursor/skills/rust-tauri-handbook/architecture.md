# Architecture Guidelines

## Verdict

**Default for large Tauri apps:** a **modular monolith** with **vertical feature slices** inside a Cargo workspace. The Tauri host is a thin composition root. Domain logic stays framework-agnostic crates. The WebView is a presentation + IPC client only.

This beats pure Clean Architecture ceremony for desktop apps (fewer layers, clearer compile boundaries) while keeping extractable seams. Prefer feature crates over deep Domain/Application/Infrastructure trees until a crate exceeds ~3–5k LOC or ownership splits.

## Trust model (Tauri v2)

```text
┌─────────────────────────────────────────────┐
│  WebView (untrusted)                        │
│  HTML/JS/React — no secrets, no privileged  │
│  I/O, no raw SQL                            │
└──────────────────┬──────────────────────────┘
                   │ IPC (commands / events / channels)
                   │ Runtime Authority + capabilities
┌──────────────────▼──────────────────────────┐
│  Rust Core (trusted)                        │
│  Commands, plugins, state, DB, crypto, HW   │
└─────────────────────────────────────────────┘
```

Sources: [Tauri Security](https://v2.tauri.app/security/), process model, capabilities.

## Pattern catalog

| Pattern                               | Use when                                   | Avoid when                                             |
| ------------------------------------- | ------------------------------------------ | ------------------------------------------------------ |
| Modular monolith + feature crates     | Product app growing past one crate         | Tiny spike / single-file demo                          |
| Vertical slice (feature folder/crate) | One team owns a capability end-to-end      | Shared kernel still unstable                           |
| Clean Architecture layers             | Strong isolation needed (crypto, payments) | Every CRUD feature                                     |
| CQRS-lite (commands vs queries)       | Writes have invariants; reads are simple   | Over-splitting trivial APIs                            |
| Repository trait                      | Swappable storage / test doubles           | Single SQLite impl forever with no tests needing fakes |
| Outbox / sync queue                   | Offline-first or unreliable network        | Always-online thin client with no local writes         |
| Plugin (Tauri plugin)                 | Reusable host capability across apps       | One-off app-private command                            |
| Event bus (Tauri events)              | Fan-out UI notifications                   | Request/response (use commands)                        |
| Channels                              | Ordered high-frequency streams             | Occasional status updates                              |

## Binexus Branch Client (specialization)

Per `docs/architecture/desktop-tauri.md` and ADRs:

| Layer         | Owns                                                                        |
| ------------- | --------------------------------------------------------------------------- |
| React UI      | Operator UX, carts, session UI, cached reads display                        |
| Rust host     | Pairing, Branch HTTP, keyring secrets, ESC/POS, serial/USB, single-instance |
| Branch Server | Business rules, commit, journal, auth, PostgreSQL                           |

**Forbidden in Tauri:** Postgres credentials, authoritative sales/stock writes without server commit.

Local SQLite (when added) = **cache + outbox**, not system of record.

## Offline-first desktop

1. Write locally first (SQLite in Rust).
2. Enqueue sync operations with idempotent `commandId` / operation keys.
3. Background sync when online; retry with backoff.
4. Conflict policy explicit: LWW, server-wins, or CRDT (only if multi-writer collaboration is required).
5. UI shows sync state (pending / conflict / synced); never pretend unconfirmed data is committed where money/stock matter.

For POS money paths: **server commit is truth**; local cart is optimistic draft.

## Multi-window

- One capability file per security posture (e.g. `main`, `settings`, `kiosk`).
- Label windows explicitly; never grant `*` permissions lightly.
- Shared state via `tauri::State` / `AppHandle` in the core process — not `localStorage` for secrets.

## State management

| Concern                | Where                                |
| ---------------------- | ------------------------------------ |
| UI ephemeral state     | Frontend (React/Zustand/etc.)        |
| Session / domain cache | Rust `State` or SQLite               |
| Secrets                | OS keyring (`keyring`) via Rust only |
| Cross-window sync      | Rust emits events; windows listen    |

## Dependency rule (crates)

```text
domain / features-*  →  no Tauri, no UI
app-services         →  domain + ports
infra-*              →  implements ports (sqlite, http, keyring)
tauri-host           →  wires everything, exposes commands
```

Cycles between crates are forbidden. Extract shared types to `*-contracts` or `shared`.
