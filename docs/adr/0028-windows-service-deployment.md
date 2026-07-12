# ADR-0028: Windows Service deployment for Branch Server

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Branch Server must run without an interactive desktop session. Tauri lifetime must not own API or Postgres lifetime.

## Decision

On the Principal, Branch Installer registers Windows Services for:

- Branch API
- Branch Workers (includes Sync Worker hosting unless split later)
- PostgreSQL (installer-managed instance)

Services start at boot. Tauri optional on the same machine is a Branch Client process, not the service host.

Updates replace service packages with stop/start or side-by-side then cutover. Risky DB migrations require backup first (ADR-0030).

## Consequences

### Positive

- Survives user logoff.
- Matches SMB always-on Principal expectation.

### Negative / Trade-offs

- Requires elevation and Windows service skill in Installer.
- Service identity/ACL design needed for secret access.

## Alternatives considered

1. **Run API only while Tauri is open** - Rejected.
2. **IIS-hosted only** - Deferred; Kestrel Windows Service is enough for v1.
3. **Linux-only Principal** - Out of scope for initial Windows focus.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
