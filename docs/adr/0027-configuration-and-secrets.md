# ADR-0027: Configuration and secrets placement

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

`config.json` is not an authorization source. Secrets on disk in plaintext create branch-wide compromise. Tauri must never see PostgreSQL credentials.

## Decision

### Matrix

| Data                                                | config local | Credential Manager / keychain | PostgreSQL |  Do not store  |
| --------------------------------------------------- | :----------: | :---------------------------: | :--------: | :------------: |
| Setup / config schema version                       |      x       |                               |            |                |
| Runtime selection (`Cloud` host vs Branch profiles) |      x       |                               |            |                |
| Branch URL / last endpoint                          |      x       |                               |            |                |
| BranchInstanceId (pointer)                          |      x       |                               |     x      |                |
| DeviceId (pointer)                                  |      x       |                               |     x      |                |
| TerminalId selection                                |      x       |                               |     x      |                |
| Server fingerprint                                  |      x       |      preferred pin copy       |            |                |
| Feature flags                                       |              |                               |     x      |                |
| Permissions / roles                                 |              |                               |     x      |                |
| User JWT access token                               |              |        short-lived ok         |            |                |
| Refresh token                                       |              |               x               |            |                |
| Device credential / private key                     |              |               x               |            |                |
| Activation code                                     |              |                               |            |  x after use   |
| DB password                                         |              |      Branch Server only       |            | never in Tauri |
| MinIO / object store secret                         |              |      Branch Server only       |            | never in Tauri |
| Sync credential                                     |              |      Branch Server only       | x metadata | never in Tauri |
| Password hashes                                     |              |                               |     x      |                |

### Rules

- `config.json` is not a source of authorization.
- Features and permissions come from backend data.
- Tokens and private keys live in OS secure storage.
- Activation codes are deleted after successful use.
- PostgreSQL credentials never ship to Tauri.
- Config schema is versioned and migratable.

## Consequences

### Positive

- Clear audit of where secrets live.
- Reduces accidental secret leakage via support logs.

### Negative / Trade-offs

- More moving parts for backup of secret material (ADR-0030).

## Alternatives considered

1. **All settings in config.json** - Rejected.
2. **Tauri holds DB password for "direct mode"** - Rejected.
3. **Keep activation codes for reinstall** - Rejected; issue new codes.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.
