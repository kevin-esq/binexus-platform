# Anti-Patterns (Forbidden)

## Security

| Anti-pattern                                   | Do instead                     |
| ---------------------------------------------- | ------------------------------ |
| `fs:default` / shell allow-all for convenience | Scoped paths; deny shell       |
| Secrets in `localStorage` / frontend state     | `keyring` via Rust             |
| Returning `anyhow`/`Display` chains to UI      | Stable `PublicError` codes     |
| Frontend raw SQL via sql plugin                | Rust repositories + commands   |
| CSP omitted                                    | Restrictive CSP always         |
| Remote capability URLs without review          | Default deny remote API access |
| `dangerousInsecureTransport` for updater       | HTTPS + signatures             |
| Logging tokens / device keys                   | Redact; structured fields only |

## Rust

| Anti-pattern                               | Do instead                                  |
| ------------------------------------------ | ------------------------------------------- |
| `unwrap`/`expect` in command handlers      | `AppResult` / `map_err`                     |
| Holding `std::sync::Mutex` across `.await` | `tokio::sync::Mutex` or restructure         |
| Blocking SQLite on async runtime thread    | `spawn_blocking`                            |
| Unbounded channels / spawn leaks           | Bounded mpsc; `JoinSet` / graceful shutdown |
| Catch-all `unsafe` for speed               | Profile first; document SAFETY invariants   |
| Circular crate deps                        | Extract `contracts`                         |
| Enabling all tokio features                | Feature-gate what you use                   |

## Architecture

| Anti-pattern                                         | Do instead                                |
| ---------------------------------------------------- | ----------------------------------------- |
| Business rules only in React                         | Server (Binexus) or Rust domain           |
| God `lib.rs` with all logic                          | Feature modules / crates                  |
| Electron mental model (Node privileges in UI)        | Trust boundary discipline                 |
| Authoritative money/stock only in local SQLite (POS) | Server commit; local = draft/cache        |
| Deep Clean Architecture for every screen             | Vertical slices; deepen when pain is real |

## Process

| Anti-pattern                                | Do instead                                       |
| ------------------------------------------- | ------------------------------------------------ |
| Adding plugin without capability permission | Three-step install (cargo + plugin + capability) |
| Ignoring `cargo audit` failures             | Fix or time-boxed ignore with owner              |
| Drive-by dependency major bumps             | Dedicated PR + audit                             |
| Skipping Clippy in CI                       | `-D warnings` gate                               |
