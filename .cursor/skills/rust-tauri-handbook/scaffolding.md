# Project Scaffolding Standards

## Bootstrap (greenfield Tauri v2)

```bash
npm create tauri-app@latest
# Select: TypeScript + React (or project frontend standard), Tauri 2
```

Then immediately:

1. Add `capabilities/main-capability.json` with **only** `core:*:default` needed
2. Set CSP in `tauri.conf.json` (`default-src 'self'`, `connect-src` for IPC)
3. Add `error.rs` with typed `AppError` + `Serialize` public DTO
4. Add `tracing` subscriber in `lib.rs` setup
5. `cargo add serde serde_json thiserror tracing`
6. Add `deny.toml` + `audit.toml`; wire CI
7. Pin `rust-toolchain.toml` (channel + components: clippy, rustfmt)
8. Commit `Cargo.lock`

## Binexus desktop (existing)

Work in `apps/desktop`. Do not re-scaffold. Extend modules (`pairing`, `secrets`, `branch`, …) and keep capabilities minimal (`main-capability.json`).

## Cargo.toml defaults

```toml
[package]
edition = "2021"          # or 2024 when toolchain supports team-wide
rust-version = "1.97.1"   # align with rust-toolchain.toml

[profile.release]
lto = "thin"
codegen-units = 1
strip = "symbols"
# opt-level = 3 default; use "s"/"z" only when size > speed
```

## Capability template

```json
{
  "$schema": "../gen/schemas/desktop-schema.json",
  "identifier": "main-capability",
  "description": "Main window — minimal core only",
  "windows": ["main"],
  "permissions": [
    "core:event:default",
    "core:window:default",
    "core:app:default",
    "core:webview:default"
  ]
}
```

## Command template (pattern)

```rust
#[tauri::command]
pub async fn example_command(
    state: tauri::State<'_, AppState>,
    input: ExampleInput,
) -> Result<ExampleOutput, PublicError> {
    state
        .services
        .example(input)
        .await
        .map_err(|e| e.public())
}
```

Register once in `generate_handler![..., example_command]`.

## Workspace upgrade path

When extracting crates:

1. Create virtual `Cargo.toml` workspace
2. Move lib to `crates/app-core`
3. Keep `tauri-host` depending on core
4. Share versions via `[workspace.dependencies]`
