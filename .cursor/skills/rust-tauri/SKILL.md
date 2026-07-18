---
name: rust-tauri
description: Tauri v2 development for Binexus Branch Client and Rust+Tauri apps — commands, IPC, events, channels, plugins, capabilities, state, CSP. Use when editing apps/desktop, src-tauri, tauri.conf.json, capabilities, #[tauri::command], invoke(), or Tauri plugins.
---

# rust-tauri (Tauri v2)

Primary skill for Tauri host work. Pair with [`rust-security`](../rust-security/SKILL.md) for permissions and [`rust-tauri-handbook`](../rust-tauri-handbook/SKILL.md) for deep standards.

## Responsibilities

- Keep WebView untrusted; put privileged work in Rust
- Expose a minimal, typed command surface
- Register plugins + capability permissions together
- Prefer async commands; stream with channels when needed

## IPC choice

| Need                        | Mechanism                                |
| --------------------------- | ---------------------------------------- |
| Request/response            | `#[tauri::command]` + `invoke`           |
| Fan-out / UI notify         | Events (`emit` / `listen`)               |
| Ordered high-frequency data | `tauri::ipc::Channel`                    |
| Large binary                | `tauri::ipc::Response` (avoid huge JSON) |

## Always

- List every command in **one** `generate_handler![]`
- Return `Result<T, PublicError>` (or equivalent serializable error)
- Validate inputs with typed structs (`Deserialize` + parse)
- Mark I/O and CPU-heavy commands `async`
- Add capability permission when adding a plugin API
- Keep `apps/desktop` capabilities minimal (see existing `main-capability.json`)

## Never

- Put secrets, private keys, or Postgres credentials in the frontend
- Call `unwrap`/`expect` in command paths
- Grant `fs`/`shell`/`http` broadly “for later”
- Block the main thread with sync SQLite/network
- Forget capability entries after `cargo add` of a plugin

## Command pattern

```rust
#[tauri::command]
pub async fn do_thing(
    state: tauri::State<'_, AppState>,
    input: DoThingInput,
) -> Result<DoThingOutput, PublicError> {
    state.core.do_thing(input).await.map_err(AppError::public)
}
```

Borrowing in async commands: prefer owned `String` over `&str`.

## Plugins (install triad)

1. Cargo + npm packages
2. `.plugin(tauri_plugin_x::init())`
3. Capability permission (`x:default` or finer)

## Binexus notes

- Pairing, Branch HTTP, and keyring stay in Rust
- Local cache is non-authoritative (`docs/architecture/desktop-tauri.md`)
- Align with existing modules: `pairing`, `secrets`, `branch`, `config`, `crypto`

## References

- [commands-ipc.md](commands-ipc.md)
- [capabilities.md](capabilities.md)
- Official: https://v2.tauri.app/develop/calling-rust/
