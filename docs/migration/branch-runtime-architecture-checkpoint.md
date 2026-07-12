# CHECKPOINT BRANCH RUNTIME ARCHITECTURE - FINAL

**Status:** Direction approved  
**Date:** 2026-07-12  
**Approved by:** Kevin Esquivel  
**Scope:** Architecture docs and ADRs only. Implementation starts only after this documentation is reviewed and merged, on new branches from `main`.

All ADRs **0016–0032** remain `Proposed` until each decision is implemented or ADR policy marks them Accepted.

---

## Executive decision summary

| Decision           | Choice                                                                                       |
| ------------------ | -------------------------------------------------------------------------------------------- |
| Installation modes | Cloud Runtime, Branch Server, Branch Client (never one vague "modo local")                   |
| Authority          | One active `BranchInstance` + one Postgres per sucursal                                      |
| Offline v1         | Internet down → keep selling; LAN to server down → no confirmed ops on that client           |
| Composition        | `AddBinexusCore` + `AddCloudRuntime` / `AddBranchRuntime`; no `if (runtimeMode)` in handlers |
| Sync               | Sync Journal + Inbox + Checkpoint; not `PendingToSync` on business rows                      |
| IDs                | UUIDv7; IDs keep origin; ownership per aggregate                                             |
| Trust ceremonies   | Activation Branch↔Cloud ≠ Pairing Client↔Branch                                              |
| LAN security       | TLS + server fingerprint + device credential + user token                                    |
| Discovery          | Candidates with fingerprint; hostname is not identity                                        |
| Provisioning       | Dedicated Branch Installer; Tauri shows progress only                                        |
| Web Admin          | Cloud only + freshness fields                                                                |
| Proofs             | Local durable object first; sync binary + metadata later                                     |
| HA                 | No multi-node / auto-failover in v1                                                          |

---

## Topología final

```text
Cloud Runtime
  Web Admin → Cloud API / Workers → PostgreSQL Cloud
        ↕ Sync Journal / Inbox / Checkpoints
Branch Server (one active per sucursal)
  Branch API + Workers + Sync Worker → PostgreSQL local
        ↑ TLS + device + user
Branch Clients (Tauri)
  Caja / Oficina  -  no Postgres, no authoritative domain copy
```

---

## Modos Cloud / Branch Server / Branch Client

See [ADR-0016](../adr/0016-three-installation-modes.md).

| Mode          | Processes                           | Role                                        |
| ------------- | ----------------------------------- | ------------------------------------------- |
| Cloud Runtime | API, Workers, PG Cloud              | SaaS, entitlements, sync hub, Web Admin     |
| Branch Server | API, Workers, Sync Worker, PG local | Authoritative sucursal ops                  |
| Branch Client | Tauri only                          | LAN UI + hardware; depends on Branch Server |

---

## Offline-first preciso

| Caso                            | Resultado                                    |
| ------------------------------- | -------------------------------------------- |
| Offline de internet             | Sucursal opera en Branch Server              |
| Offline del Branch Server (LAN) | Branch Client no confirma operaciones nuevas |

Confirmación = commit en Branch Server + PostgreSQL. Modo terminal degradado con DB propia = futuro ADR.

---

## Autoridad de datos (muestra)

| Tipo                                                            | Autoridad | Dirección |
| --------------------------------------------------------------- | --------- | --------- |
| Venta presencial / sesión caja / movimiento físico / ruta local | Branch    | → Cloud   |
| Entitlement / activación / catálogo publicado / config global   | Cloud     | → Branch  |
| Pedido e-commerce create                                        | Cloud     | → Branch  |
| Estado operativo tras accept                                    | Branch    | → Cloud   |

---

## Matriz de capacidades (resumen)

| Capacidad                                | Cloud | Branch Server | Shared |
| ---------------------------------------- | :---: | :-----------: | :----: |
| SaaS admin / entitlements / Web Admin    |   x   |               |        |
| Sync ingest / publish                    |   x   |               |        |
| In-person commit / LAN API / Sync Worker |       |       x       |        |
| Domain modules / handlers / EF           |       |               |   x    |

Full matrix in ADR-0016.

---

## Identidad

`BranchId` · `BranchInstanceId` · `DeviceId` · `TerminalId` · `UserId` - [ADR-0018](../adr/0018-device-terminal-user-identity.md).

---

## Activación Branch ↔ Cloud

Web Admin code → Installer/Wizard → `BranchInstanceId` + credentials + bootstrap - [ADR-0019](../adr/0019-branch-server-activation.md).

Second Active instance blocked; Replace is explicit - [ADR-0017](../adr/0017-single-branch-instance.md).

---

## Pairing Device ↔ Branch

Separate from activation. Fingerprint confirm, device credential, terminal assignment - [ADR-0020](../adr/0020-branch-client-pairing.md).

---

## Seguridad LAN + certificados

```text
TLS local
+ Branch Server identity (pinned fingerprint)
+ device credential
+ user access token
```

Bind configured interfaces; Private firewall; min client version - [ADR-0023](../adr/0023-lan-api-security.md).

Trust without public CA: pairing ceremony pins fingerprint.

---

## Discovery / fallback

Candidates: display name, InstanceId fragment, address, version, fingerprint. Manual fallback always - [ADR-0021](../adr/0021-lan-discovery.md).

---

## Installer boundary

**Binexus Branch Installer** owns Postgres, services, firewall, migrations. Tauri invokes + progress UX only - [ADR-0022](../adr/0022-branch-installer.md), [ADR-0028](../adr/0028-windows-service-deployment.md).

---

## Configuración y secretos

Matrix in [ADR-0027](../adr/0027-configuration-and-secrets.md). Highlights: no auth in `config.json`; activation code not retained; DB secrets never in Tauri.

---

## Bootstrap resumible

Activate → credentials → manifests → batches → checksum → checkpoint → resume → Ready. Generic UX phases only - [ADR-0026](../adr/0026-resumable-bootstrap.md).

---

## Sync journal / upstream / downstream

Outbox ≠ Sync Journal ≠ Inbox ≠ Checkpoint - [ADR-0025](../adr/0025-sync-journal-ownership-conflicts.md).

Examples in ADR: upstream sale, downstream catalog, e-commerce order (owner, id, version, idempotency, order, conflict, retry, terminal).

---

## Proof object sync

Local durable object first; metadata via journal; binary pipeline with resume/checksum; Web shows pending until Cloud has object - [ADR-0029](../adr/0029-proof-object-sync.md).

---

## Backup / recovery

Daily PG + objects; restore vs checkpoint; idempotent replay; Replace on new hardware; admin conflict if diverge - [ADR-0030](../adr/0030-backup-and-recovery.md).

---

## Update / version compatibility

Separate channels: Tauri, Branch Runtime, PG migration, sync protocol. Min desktop version; one stale client must not stop the sucursal - [ADR-0031](../adr/0031-update-version-compatibility.md).

---

## ADRs fusionados / conservados

| ADR  | Título                   | Decisión principal                                                            | Alternativas descartadas                                       | Dependencias     | Riesgo                       |
| ---- | ------------------------ | ----------------------------------------------------------------------------- | -------------------------------------------------------------- | ---------------- | ---------------------------- |
| 0016 | Three installation modes | Cloud / Branch Server / Branch Client + composition roots + capability matrix | Vague "local mode"; `if(runtime)` in handlers; forked products | 0015             | Wrong mode naming in UX      |
| 0017 | Single BranchInstance    | One active instance + Replace; no HA cluster                                  | Dual live servers; auto-failover                               | 0016, 0019       | Accidental second Principal  |
| 0018 | Device/Terminal/User     | Distinct ids; UUIDv7 keep-origin; ownership matrix                            | Device=Terminal; ID remap; LWW default                         | 0013, 0017       | Taxonomy confusion           |
| 0019 | Branch activation        | Cloud ceremony for BranchInstance                                             | Same handshake as pairing                                      | 0017, 0022, 0026 | Activation vs pairing mixups |
| 0020 | Client pairing           | Local ceremony + device credential                                            | Trust mDNS; user JWT only                                      | 0018, 0021, 0023 | LAN spoofing                 |
| 0021 | Discovery                | Candidates + fingerprint; manual fallback                                     | Hostname as identity                                           | 0020, 0023       | Colliding `.local`           |
| 0022 | Branch Installer         | Dedicated elevated installer; Tauri progress only                             | Tauri provisions Postgres                                      | 0028             | Unsafe ad hoc scripts        |
| 0023 | LAN API security         | TLS + pin + device + user                                                     | HTTP+JWT only; public CA `.local`                              | 0020, 0021       | Weak LAN auth                |
| 0024 | Offline internet vs LAN  | Honest v1 offline boundary                                                    | Per-terminal confirmed sales                                   | 0003, 0017       | Overpromised offline         |
| 0025 | Sync journal + conflicts | Journal/inbox/checkpoint; 3 worked examples                                   | PendingToSync flags; LWW money/stock                           | 0004, 0018       | Sync coupling / silent merge |
| 0026 | Resumable bootstrap      | Phased checkpoints; generic UX                                                | Monolithic download; fake SKU SLAs                             | 0019             | Stuck half-open branch       |
| 0027 | Config & secrets         | Placement matrix                                                              | Secrets in config.json; DB pw in Tauri                         | 0023             | Secret leakage               |
| 0028 | Windows Services         | API/Workers/PG as services                                                    | API lifetime tied to Tauri                                     | 0022             | Principal stops on logoff    |
| 0029 | Proof objects            | Local first + later Cloud object sync                                         | Cloud-required for delivery complete                           | 0025             | Disk growth / pending UX     |
| 0030 | Backup & recovery        | PG+objects; restore vs sync                                                   | Cloud sync as only backup                                      | 0017, 0025       | Data loss on disk failure    |
| 0031 | Version compatibility    | Channel matrix; isolate stale clients                                         | Lockstep all cashiers; no min version                          | 0020, 0025       | Bricked branch on one client |
| 0032 | Web Admin freshness      | Cloud-only web; freshness fields                                              | Web→LAN; hide staleness                                        | 0016, 0025       | False live stock in cloud    |

**Merges vs prior draft:** responsibilities→0016; device+terminal→0018; HTTP+auth→0023; sync+conflicts→0025; config+secrets→0027; installation topology split across 0017/0022/0028. **New:** proof objects, backup, updates, web freshness; activation≠pairing split.

---

## Roadmap por PRs

Implementation sequencing lives in [`docs/architecture/branch-runtime-roadmap.md`](../architecture/branch-runtime-roadmap.md). Do not start PR 1 until this checkpoint documentation is merged.

---

## Riesgos bloqueantes

1. Accidental second Active BranchInstance without Replace discipline.
2. Shipping HTTP+JWT without device credentials on real Wi-Fi.
3. Overpromising per-terminal offline confirmed sales.
4. Implementing sync with `PendingToSync` on aggregates.
5. Letting Tauri provision Postgres.
6. Treating discovery hostname as trust.
7. No backup story before real branch pilots.
8. Web Admin implied live data without freshness.

---

## Approval checklist

- [x] Three modes naming locked
- [x] Offline internet vs LAN locked
- [x] Single BranchInstance + Replace locked
- [x] Composition roots locked
- [x] Sync journal (not entity flags) locked
- [x] Activation ≠ pairing locked
- [x] LAN security stack locked
- [x] Installer boundary locked
- [x] Config/secrets matrix locked
- [x] Bootstrap resumable locked
- [x] Proof/backup/update/web freshness ADRs locked
- [x] PR roadmap accepted as sequencing guide

## Explicitly not in this checkpoint

No Branch Runtime, Tauri, sync, installer, hardware, Stripe, EF, Docker, or CI implementation in the documentation PR. Implementation uses new branches from updated `main` after merge.
