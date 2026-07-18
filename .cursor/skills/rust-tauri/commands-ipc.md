# Commands and IPC

## Registration

One handler per app:

```rust
.invoke_handler(tauri::generate_handler![
    commands::a,
    commands::b,
])
```

Multiple `invoke_handler` calls — last wins; do not split.

## Errors

Map domain errors to a serializable public DTO:

```rust
#[derive(Serialize)]
pub struct PublicError {
    pub code: &'static str,
    pub message: String,
}
```

Frontend branches on `code`, not English `message`.

## State

```rust
app.manage(AppState::new(...));
// in command:
state: tauri::State<'_, AppState>
```

Share DB pools, clients, and config here — not in JS globals.

## Events

- Not type-safe; JSON only; no return value
- Good for “pairing status changed”
- Bad for “upload this file and get result”

## Channels

Pass `Channel<T>` into commands for progress/chunks. Prefer over rapid `emit` for ordered streams.

## AppManifest (hardening)

Optionally restrict which commands exist via `build.rs` `AppManifest::commands(&[...])`, then allow per-window in capabilities.
