# Desktop Tauri architecture (Branch Client)

Tauri is the **Branch Client** shell: POS, warehouse, logistics, inventory, and host hardware integration. It is not Branch Server.

Related: [branch-runtime.md](./branch-runtime.md), [ADR-0020](../adr/0020-branch-client-pairing.md), [ADR-0023](../adr/0023-lan-api-security.md), [ADR-0027](../adr/0027-configuration-and-secrets.md).

## Process shape

```mermaid
flowchart LR
    subgraph tauri[Branch Client - Tauri]
        ui[React UI]
        rust[Rust host]
        ui <-->|IPC hardware / secure store| rust
    end

    subgraph server[Branch Server]
        api[Branch API]
        pg[(PostgreSQL)]
        api --> pg
    end

    ui -->|TLS + device credential + user token| api
```

## Responsibilities

| Layer         | Owns                                                     |
| ------------- | -------------------------------------------------------- |
| React         | Operator UX, carts, sessions UI, cached reads            |
| Rust          | ESC/POS, serial/USB, OS secure storage, installer invoke |
| Branch Server | Business rules, commit, journal, auth                    |

Tauri never receives PostgreSQL credentials and never writes the branch database directly.

## Local cache (non-authoritative)

Allowed: unconfirmed cart, config, visual session, cached reads, retry of lost responses with same `commandId`.

Not allowed as confirmed truth: sales or stock without Branch Server commit (ADR-0024).

## Wizard roles

| Path                | Tauri role                                  |
| ------------------- | ------------------------------------------- |
| Principal first run | Invoke Branch Installer, then activation UX |
| Secondary cashier   | Pairing UX only                             |
| Cloud operator      | Not Tauri; Web Admin                        |
