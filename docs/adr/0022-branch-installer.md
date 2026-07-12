# ADR-0022: Branch Installer and wizard contract

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Elevating PostgreSQL, Windows Services, firewall rules, and migrations from ad hoc Tauri/Rust scripts is unsafe and hard to support.

## Decision

Ship a dedicated **Binexus Branch Installer** component (MSI/EXE or equivalent elevated package).

### Installer owns

- Administrative elevation
- Prerequisites
- Local PostgreSQL install and random credentials
- Database create + migrations
- Branch API, Workers, Sync Worker binaries
- Windows Service registration
- Firewall rules (private LAN profiles)
- Directories, logs
- Initial backup hook
- Install rollback on failure

### Tauri (wizard) may

```text
invoke installer
→ receive structured progress events
→ render UX
```

Rust does not become the provisioning engine. Branch Client installs may ship Tauri alone without the full Installer.

### Wizard flows

1. **Activate Branch Server** - launch Installer, then ADR-0019 activation, then ADR-0026 bootstrap.
2. **Pair Branch Client** - ADR-0020 only (no Postgres install).
3. **Cloud-only operator** - browser Web Admin; no Branch Installer.

## Consequences

### Positive

- Supportable elevated install path.
- Clear security boundary for DB passwords (never exposed to Tauri).

### Negative / Trade-offs

- Extra artifact to sign and version.
- Progress IPC contract to design carefully.

## Alternatives considered

1. **Tauri embeds Postgres provisioning scripts** - Rejected.
2. **Manual-only IT install with no wizard** - Deferred as advanced path; wizard still needs Installer.
3. **Docker-only Principal on Windows SMB** - Rejected for v1 target operators.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
