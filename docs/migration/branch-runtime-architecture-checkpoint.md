# CHECKPOINT — Branch Runtime Architecture

**Status:** Proposed — awaiting direction approval  
**Date:** 2026-07-12  
**Scope:** Architecture only: ADRs and docs. No implementation.

Explicit non-goals:

- No sync code.
- No hardware integration.
- No Tauri shell.
- No wizard implementation.
- No Stripe or billing work.
- No domain module rewrites.

---

## Target topology

```text
Cloud
  Web Admin / SaaS control plane
  Cloud Backend (.NET, Cloud runtime)
  Managed PostgreSQL
  Sync ingest + downstream publishing
       ^
       | async upstream/downstream sync
       v
Branch / Sucursal
  Servidor Principal
    Branch Backend (.NET, Branch runtime)
    Branch Workers
    PostgreSQL (one database for the branch)
    Optional Tauri terminal on same machine
       ^
       | LAN HTTP API
       v
Tauri terminals
  Caja 1
  Caja 2
  Oficina
```

Cloud handles tenant administration, billing, supervision, synced reporting, and sync hub responsibilities. Branch handles in-person operational writes. Tauri terminals call the Branch API over the LAN.

## Locked invariants

| Invariant                 | Decision                                                                                                            |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| Branch authority          | A Branch runtime owns in-person operational commands for its sucursal.                                              |
| No Tauri to PostgreSQL    | Tauri never receives PostgreSQL credentials and never writes the database directly.                                 |
| One PostgreSQL per branch | One local PostgreSQL instance serves one `BranchInstance` for one sucursal.                                         |
| Cloud not on sale path    | `CreateSale` and other in-person commands commit locally and do not wait for Cloud.                                 |
| Composition roots         | Runtime-specific behavior belongs in `AddCloudRuntime()` or `AddBranchRuntime()`, not inside shared domain modules. |

## Identity taxonomy

| Concept            | Meaning                                                                                               | Authority                                                              |
| ------------------ | ----------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Sucursal           | Business branch or location within a tenant.                                                          | Cloud creates and administers it.                                      |
| BranchInstance     | Installed runtime instance for one sucursal, including its Branch Server identity and local database. | Cloud provisions it; Branch stores and uses it locally.                |
| Servidor Principal | Physical or virtual machine that runs Branch Server, Branch Workers, and PostgreSQL.                  | Branch installation owns it.                                           |
| Caja/Terminal      | Logical POS or workstation role, such as Caja 1, Caja 2, Oficina, or Mostrador.                       | Branch policy assigns it to devices.                                   |
| Device             | Machine identity for a Tauri installation or Branch Server host.                                      | Pairing creates it; revocation targets it.                             |
| User               | Human actor with roles and permissions.                                                               | Cloud provisions users; Branch authenticates locally from synced data. |

## ADR index

| #    | ADR                                                                                          | One-line decision                                                                                           |
| ---- | -------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| 0016 | [Runtime modes for Cloud and Branch](../adr/0016-runtime-modes-cloud-vs-branch.md)           | Select Cloud or Branch through explicit .NET composition roots.                                             |
| 0017 | [Branch runtime responsibilities](../adr/0017-branch-runtime.md)                             | Split Branch-owned, Cloud-owned, and shared backend responsibilities.                                       |
| 0018 | [Branch Server per sucursal](../adr/0018-branch-server.md)                                   | Run one Branch Server and one PostgreSQL database per sucursal.                                             |
| 0019 | [Device identity for Branch and Tauri hosts](../adr/0019-device-identity.md)                 | Give every Branch Server and Tauri installation a dedicated device identity.                                |
| 0020 | [Terminal identity as a logical POS role](../adr/0020-terminal-identity.md)                  | Treat terminal identity as a business role, not a machine credential.                                       |
| 0021 | [LAN discovery for Branch Server](../adr/0021-lan-discovery.md)                              | Use mDNS/DNS-SD first, with QR, manual address, and local DNS fallbacks.                                    |
| 0022 | [Branch device pairing and handshake](../adr/0022-pairing-and-handshake.md)                  | Use short-lived Cloud-approved pairing to create durable device credentials.                                |
| 0023 | [Branch installation topology](../adr/0023-branch-installation.md)                           | Install Branch Server, PostgreSQL, and services on the Principal; install Tauri only on secondary cashiers. |
| 0024 | [Branch local HTTP API](../adr/0024-local-http-api.md)                                       | Expose Branch operations over a LAN HTTP API, preferably HTTPS with local trust.                            |
| 0025 | [Branch local authentication](../adr/0025-local-authentication.md)                           | Authenticate users locally with synced credentials and branch-specific JWT keys.                            |
| 0026 | [Offline-first strategy for Branch Runtime](../adr/0026-offline-first-strategy.md)           | Make Branch the local authority and sync asynchronously with Cloud.                                         |
| 0027 | [Branch and Cloud synchronization architecture](../adr/0027-synchronization-architecture.md) | Run Branch-side sync workers that push upstream facts and pull downstream data.                             |
| 0028 | [Branch Runtime conflict resolution](../adr/0028-conflict-resolution.md)                     | Use explicit authority and admin-visible conflicts instead of silent stock or money merges.                 |
| 0029 | [Branch Runtime bootstrap snapshot](../adr/0029-bootstrap.md)                                | Bootstrap Principals from Cloud and Secondary cashiers from the Principal.                                  |
| 0030 | [Branch Runtime configuration storage](../adr/0030-configuration-storage.md)                 | Store non-secret startup config in files, secrets in the OS secure store, and domain state in PostgreSQL.   |
| 0031 | [Branch Runtime secrets storage](../adr/0031-secrets-storage.md)                             | Use Windows Credential Manager or DPAPI and Tauri keychain access for local secrets.                        |
| 0032 | [Branch Runtime Windows Service deployment](../adr/0032-windows-service-deployment.md)       | Run Branch API, Workers, and PostgreSQL as installer-managed Windows Services on the Principal.             |

Repo listing confirmed `docs/adr/0016*.md` through `0032*.md`.

## Architecture docs index

| Doc                                                                   | Purpose                                                                                                       |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| [Branch Runtime architecture](../architecture/branch-runtime.md)      | Target Cloud, Branch, and Tauri topology with runtime modes, identity, local API, discovery, and sync shape.  |
| [Desktop Tauri architecture](../architecture/desktop-tauri.md)        | Desktop process split between React, Rust host duties, Branch API calls, and hardware integration boundaries. |
| [Branch Wizard UX specification](../architecture/branch-wizard-ux.md) | First-run role selection, LAN discovery, manual pairing, bootstrap, and recovery UX.                          |
| [Web vs Desktop surfaces](../architecture/web-vs-desktop-surfaces.md) | Migration rules for which workflows move to Desktop and which stay in Web.                                    |

## Process map: Principal vs Secondary

| Component          | Servidor Principal                                          | Caja Secundaria                          |
| ------------------ | ----------------------------------------------------------- | ---------------------------------------- |
| Branch API         | Runs as a service                                           | Calls over LAN                           |
| Branch Workers     | Runs outbox, inbox, scheduler, diagnostics, and future sync | Not installed                            |
| PostgreSQL         | Installed locally and owned by the branch                   | Not installed                            |
| Tauri              | Optional, if the Principal also serves an operator          | Installed                                |
| Hardware host      | Optional through Tauri Rust commands                        | Installed through Tauri Rust commands    |
| Device identity    | Principal device and `BranchInstanceId`                     | Paired terminal device                   |
| User login         | Local Branch Identity                                       | Local Branch Identity through Branch API |
| Operational writes | Commits locally                                             | Sends commands to Branch API             |

## Runtime modes design summary

`BINEXUS_RUNTIME_MODE=Cloud|Branch` selects the runtime composition root. Cloud mode registers SaaS administration, billing, tenant-wide configuration, public ingress, managed infrastructure, and sync hub endpoints. Branch mode registers local API, local workers, local authentication, discovery, branch diagnostics, local PostgreSQL bindings, and future sync jobs.

Shared modules keep business rules, command handlers, EF mappings, events, validation, and outbox contracts. Runtime-specific service selection belongs at startup and infrastructure boundaries.

## Discovery and pairing summary

Tauri discovers Branch Servers with mDNS/DNS-SD when the LAN supports it. The setup flow must also support QR payloads, manual `IP:port`, optional local DNS names, and cloud-assisted lookup where appropriate. Discovery only finds candidates; it does not prove trust.

Pairing uses a short-lived, single-use, rate-limited artifact that Cloud validates against tenant and branch ownership. Successful pairing creates durable `DeviceId`, `BranchInstanceId`, credentials, certificate or token material, and role metadata. After pairing, the terminal verifies the Branch API and uses local credentials plus user login for normal operations.

## Sync design summary

This checkpoint describes sync design only.

Upstream sync sends committed Branch facts to Cloud:

- Sales, payments, sales sessions, and cash reconciliation facts.
- Stock movements, warehouse facts, logistics facts, and proof metadata.
- Outbox events with `eventId`, `commandId`, `tenantId`, `branchId`, and `branchInstanceId`.
- Health and sync telemetry when needed.

Downstream sync sends Cloud-owned reference and policy data to Branch:

- Catalog, products, prices, promotions, and configuration.
- Users, roles, branch assignments, feature flags, and revocations.
- Device policy and tenant settings.

Sync uses checkpoints, batching, idempotency keys, priorities, retry with backoff, dead-letter handling, compression where useful, and lag observability. Request handlers do not run sync work before returning in-person command results.

## Installation, update, rollback, backup, and recovery

Principal installation places Branch Server, Branch Workers, PostgreSQL, local configuration, service credentials, firewall rules, certificates, and optional Tauri on one machine. Secondary installation places Tauri only.

Updates target the Principal runtime and Tauri clients separately. Principal updates replace service packages and run compatible database migrations. Tauri updates keep device identity and user-facing configuration.

Rollback returns the runtime package to the prior compatible version. Irreversible migrations require a backup checkpoint before upgrade.

Backups capture PostgreSQL data and branch secret material required to restore `BranchInstanceId`, local signing keys, device credentials, and sync checkpoints. Recovery restores data on the same or replacement Principal, then resumes sync idempotently.

## Risks

- Branch Runtime adds local operations: installation, backups, certificates, firewall rules, monitoring, and recovery.
- Principal failure stops local terminals until recovery or future failover exists.
- Cloud reports can lag Branch reality while sync catches up.
- User revocation and price changes can arrive late during outages.
- Certificate and pairing UX can create support load if the happy path fails on customer LANs.
- Runtime composition mistakes can expose Cloud-only services in Branch or Branch-only services in Cloud.

## Technical roadmap after approval

1. Branch Runtime.
2. Branch API.
3. Desktop Tauri shell.
4. Wizard.
5. Pairing.
6. Multi-terminal LAN.
7. Hardware.
8. Sync.
9. Cloud integration.

## Approval checklist

- [ ] Approve one Branch Server and one PostgreSQL database per sucursal.
- [ ] Approve no Tauri direct PostgreSQL access.
- [ ] Approve Cloud not participating in in-person sale requests.
- [ ] Approve Cloud/Branch runtime composition roots.
- [ ] Approve Device, Terminal, User, Branch, and BranchInstance taxonomy.
- [ ] Approve Branch local authentication with branch-specific signing keys.
- [ ] Approve LAN HTTP API as the terminal protocol.
- [ ] Approve mDNS-first discovery with required fallback paths.
- [ ] Approve Cloud-authorized pairing.
- [ ] Approve asynchronous sync design and conflict-surfacing policy.

## Explicitly not in this checkpoint

- No Branch runtime code.
- No local API endpoints.
- No sync worker implementation.
- No Cloud sync endpoints.
- No Tauri shell or Rust commands.
- No installer.
- No wizard screens beyond architecture references.
- No hardware drivers, ESC/POS, scales, scanners, or cash drawer support.
- No Stripe integration.
- No schema migrations.
- No domain module rewrites.
- No CI, packaging, signing, or release automation changes.
