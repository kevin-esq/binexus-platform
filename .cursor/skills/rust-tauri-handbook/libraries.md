# Recommended Libraries

Only mature, actively maintained crates. Prefer defaults below unless a measured need forces an alternative.

## Core / platform

| Need               | Crate                            | Why                                                                                                  |
| ------------------ | -------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Async runtime      | `tokio`                          | De-facto standard; Tauri integrates well; feature-gate (`rt-multi-thread`, `macros`, `sync`, `time`) |
| Serialization      | `serde` + `serde_json`           | Universal IPC/JSON boundary                                                                          |
| Errors (libs)      | `thiserror`                      | Typed enums, `Display`, `From` — matches Binexus `AppError`                                          |
| Errors (bin edge)  | `anyhow` optional                | Context chains; **Binexus prefers typed `AppError` end-to-end** for IPC                              |
| Logging            | `tracing` + `tracing-subscriber` | Structured, async-aware; replaces `log`/`println!`                                                   |
| Dates              | `chrono` or `time`               | `chrono` already in Branch Client; pick one per workspace                                            |
| IDs                | `uuid` (v7)                      | Time-sortable IDs for commands/outbox                                                                |
| HTTP client        | `reqwest` + `rustls-tls`         | Async; avoid OpenSSL build pain (`default-features = false`)                                         |
| URL policy         | `url`                            | Parse/validate before fetch                                                                          |
| Sync mutex (short) | `parking_lot`                    | Faster `Mutex` for non-async guards                                                                  |
| Async mutex        | `tokio::sync::Mutex`             | Only when held across `.await`                                                                       |

## Desktop / Tauri

| Need                         | Choice                          | Why                                                                           |
| ---------------------------- | ------------------------------- | ----------------------------------------------------------------------------- |
| Framework                    | `tauri` 2.x                     | Official v2; capabilities, plugins                                            |
| Build                        | `tauri-build`                   | ACL schema generation                                                         |
| Secrets                      | `keyring`                       | OS credential store (Windows Credential Manager, Keychain, Secret Service)    |
| Single instance              | `fs2` lock or official plugin   | Branch Client uses `fs2`; document choice                                     |
| FS / dialog / notify / shell | Official `@tauri-apps/plugin-*` | Permission-gated; prefer over rolling custom                                  |
| Updater                      | `tauri-plugin-updater`          | Signed artifacts; never `dangerousInsecureTransport` in prod                  |
| SQL plugin                   | `tauri-plugin-sql`              | Only if frontend must query; **prefer Rust-owned rusqlite/sqlx** for security |

## Persistence

| Need                  | Crate                                    | Why                                                     |
| --------------------- | ---------------------------------------- | ------------------------------------------------------- |
| Sync SQLite           | `rusqlite` (+ `bundled`)                 | Full SQLite control; run in `spawn_blocking`            |
| Async SQLite/Postgres | `sqlx`                                   | Compile-time checked queries; good for server-side Rust |
| Migrations            | `rusqlite_migration` or `sqlx::migrate!` | Versioned schema                                        |
| Embedded KV           | `tauri-plugin-store` or `sled`           | Simple settings — not for relational domain data        |

**Rule:** Prefer `rusqlite` inside Rust commands for desktop local DB. Do not expose ad-hoc SQL from the WebView unless scopes are extremely tight and reviewed.

## Validation / config

| Need              | Crate                               | Why                                  |
| ----------------- | ----------------------------------- | ------------------------------------ |
| Config merge      | `figment` or hand-rolled typed load | Type-safe layered config             |
| Schema validation | `validator` or custom `TryFrom`     | Parse, don't validate stringly types |
| CLI (tools)       | `clap`                              | Standard for auxiliary binaries      |

## Crypto / auth

| Need                      | Crate              | Why                             |
| ------------------------- | ------------------ | ------------------------------- |
| ECDSA P-256               | `p256`             | Branch Client pairing           |
| Hashing                   | `sha2`             | Digests                         |
| Encoding                  | `base64`, `hex`    | Wire formats                    |
| Password hash (if needed) | `argon2`           | Prefer over bcrypt for new code |
| TLS                       | rustls via reqwest | Fewer native deps               |

Avoid inventing crypto. Review `unsafe` and FFI carefully.

## Testing

| Need           | Crate                                                | Why                                  |
| -------------- | ---------------------------------------------------- | ------------------------------------ |
| Temp dirs      | `tempfile`                                           | Isolation                            |
| HTTP mock      | `wiremock`                                           | Branch Client already uses it        |
| Snapshots      | `insta`                                              | Golden files for serializable output |
| Property tests | `proptest`                                           | Invariants                           |
| Benchmarks     | `criterion`                                          | Perf regressions                     |
| E2E            | WebdriverIO + `@wdio/tauri-service` / `tauri-driver` | Official Tauri path                  |

## Observability / supply chain

| Need             | Tool                    | Why                                 |
| ---------------- | ----------------------- | ----------------------------------- |
| Advisories       | `cargo-audit`           | RustSec DB                          |
| Policy           | `cargo-deny`            | Licenses, bans, sources, duplicates |
| Unsafe inventory | `cargo-geiger`          | Spot-check deps                     |
| Size             | `cargo-bloat`, `twiggy` | Binary analysis                     |

## Explicitly not default

| Crate / approach                                  | Reason                        |
| ------------------------------------------------- | ----------------------------- |
| `openssl-sys` in desktop clients                  | Prefer rustls                 |
| `tauri-plugin-sql` as primary data API            | Expands XSS → DB blast radius |
| `unwrap`/`expect` in command paths                | Use `AppResult`               |
| Unbounded `tokio::spawn` without JoinSet/shutdown | Leaks and orphan tasks        |
| Electron patterns (Node in main)                  | Wrong trust model for Tauri   |
