---
name: rust-fundamentals
description: Idiomatic Rust standards — ownership, errors, async/Tokio, modules, features, serde, testing idioms, Clippy. Use when writing or reviewing Rust outside Tauri-specific concerns, teaching Rust conventions, or fixing borrow/async/error issues.
---

# rust-fundamentals

## Always

- Prefer owned data at async boundaries (`String`, `Vec`, `Arc`)
- `Result`/`Option` — no silent discard; `#[must_use]` respected
- Library errors: `thiserror` enums; apps may use typed `AppError` end-to-end (Binexus)
- `tracing` for logs; never `println!` in library code
- Feature-gate Tokio and heavy deps
- Follow [Rust API Guidelines](https://rust-lang.github.io/api-guidelines/) for public APIs
- `cargo fmt` + Clippy `-D warnings`

## Never

- `unwrap`/`expect` in production paths (tests OK with context)
- Hold `std::sync::Mutex` across `.await`
- Ignore `JoinHandle` errors (double-`?` on spawn)
- Enable `tokio` `full` features by default
- Unbounded growth of tasks without structured concurrency (`JoinSet`)

## Error handling

| Context       | Approach                                     |
| ------------- | -------------------------------------------- |
| Domain / libs | `thiserror`                                  |
| IPC edge      | Map to `PublicError`                         |
| Spawned tasks | `.await` handle; context on both layers      |
| Panics        | Reserve for bugs/invariants — not user input |

## Async

- Timeouts on network: `tokio::time::timeout`
- Backpressure: bounded channels
- Blocking CPU/SQLite: `spawn_blocking`
- Shutdown: `watch` / `CancellationToken` / `select!`

## Modules & visibility

- Small `pub` surface; prefer `pub(crate)`
- `mod` tree mirrors features, not arbitrary layers

## Testing

- Unit tests next to code (`#[cfg(test)]`) or `tests/` for integration
- Prefer real temp files over heavy mocks when cheap

## Common mistakes

- Fighting the borrow checker with `clone` spam — restructure lifetimes/`Arc`
- Using `anyhow` in public library APIs
- Mixing sync and async locks incorrectly
