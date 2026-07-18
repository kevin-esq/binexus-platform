# Folder Structures

## Small (MVP / spike)

Single crate inside `src-tauri`. No workspace.

```text
app/
├── package.json
├── src/                    # frontend
└── src-tauri/
    ├── Cargo.toml
    ├── tauri.conf.json
    ├── capabilities/
    │   └── main.json
    ├── icons/
    └── src/
        ├── main.rs
        ├── lib.rs          # Builder + plugins + generate_handler!
        ├── commands/
        │   └── mod.rs
        ├── error.rs
        └── state.rs
```

## Medium (product)

Feature modules under `src/`, still one package (or start extracting crates).

```text
src-tauri/src/
├── lib.rs
├── main.rs
├── error.rs
├── state.rs
├── commands/               # thin IPC adapters only
│   ├── mod.rs
│   ├── pairing.rs
│   └── hardware.rs
├── pairing/                # feature logic
│   ├── mod.rs
│   ├── ceremony.rs
│   └── orchestrator.rs
├── config/
├── secrets/
├── branch/                 # HTTP client to server
└── crypto/
```

This matches Binexus `apps/desktop/src-tauri` today.

## Large (workspace)

Virtual workspace; flat crates (Microsoft Pragmatic Rust Guidelines: M-CRATES-FLAT-FOLDER).

```text
desktop/   # or repo root for a pure Rust+Tauri product
├── Cargo.toml              # [workspace] + workspace.dependencies + lints
├── Cargo.lock
├── deny.toml
├── package.json            # frontend workspace member optional
├── apps/
│   └── desktop-ui/         # Vite/React
└── crates/
    ├── tauri-host/         # binary + lib: Tauri builder, commands
    ├── app-core/           # use cases / orchestration
    ├── domain-orders/      # pure domain (optional split)
    ├── domain-inventory/
    ├── infra-sqlite/
    ├── infra-http/
    ├── infra-keyring/
    └── contracts/          # shared DTOs / error codes for IPC
```

## Enterprise

Add packaging, tooling, and policy crates:

```text
crates/
  tauri-host/
  app-core/
  features-*/               # one crate per major capability
  infra-*/
  contracts/
  desktop-cli/              # ops utilities
tools/
  xtask/                    # cargo xtask for release/sign
.github/workflows/
  rust-ci.yml
  tauri-release.yml
```

## Frontend colocation (Binexus monorepo)

```text
apps/desktop/
├── package.json
├── src/                    # React UI
└── src-tauri/              # Rust host (may later become workspace member)
```

When Rust grows past ~one feature area, promote `src-tauri` members into `crates/` and keep `tauri-host` as the only crate that depends on `tauri`.

## Naming

| Kind         | Pattern                                            |
| ------------ | -------------------------------------------------- |
| Commands     | `snake_case` fn names = invoke names               |
| Error codes  | `SCREAMING_SNAKE` stable strings                   |
| Crates       | `kebab-case` dirs, `snake_case` lib names          |
| Capabilities | `{window}-{role}.json` e.g. `main-capability.json` |
