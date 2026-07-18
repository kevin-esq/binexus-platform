---
name: rust-tauri-performance
description: Performance standards for Rust and Tauri — startup, IPC payload size, async, SQLite, binary size (LTO/strip), compile times. Use when optimizing desktop startup, reducing bundle size, fixing UI jank from IPC, or tuning Cargo release profiles.
---

# rust-tauri-performance

## Always

- Async commands for I/O; `spawn_blocking` for sync SQLite/CPU
- Keep IPC payloads small; use channels/chunking for streams
- Measure before micro-optimizing (`criterion`, tracing spans)
- Use `lto = "thin"` + `strip` for release when size/speed matter
- Split crates to improve incremental compile when the graph is hot

## Never

- Block UI thread with network or disk
- Ship huge JSON via invoke when binary/`Response` fits
- Enable fat LTO + `codegen-units = 1` on every debug build
- Premature `unsafe` for speed

## Release profile (shipping)

```toml
[profile.release]
lto = "thin"
codegen-units = 1
strip = "symbols"
# opt-level = "s" or "z" only if binary size is the primary goal
```

## IPC

| Problem               | Fix                                         |
| --------------------- | ------------------------------------------- |
| Large file transfer   | Channel chunks / `Response`                 |
| Chatty status updates | Throttle or batch events                    |
| UI freeze             | Ensure command is async; move work off main |

## Startup

- Lazy-init heavy plugins/clients
- Defer non-critical disk reads
- Avoid sync work in `setup` hooks beyond essentials

## Tools

`cargo bloat`, `twiggy`, `criterion`, tracing spans around hot paths.
