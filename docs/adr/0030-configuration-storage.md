# ADR-0030: Branch Runtime configuration storage

| Field    | Value                                             |
| -------- | ------------------------------------------------- |
| Status   | Proposed                                          |
| Date     | 2026-07-12                                        |
| Deciders | Kevin Esquivel                                    |
| Tags     | branch, configuration, secrets, postgres, windows |

## Context and problem statement

Branch Runtime stores configuration across three surfaces: local files, the operating system secret store, and PostgreSQL. Each surface has different backup, restore, security, and editability properties. Without a clear placement rule, installers and developers can put secrets in `config.json`, mutable domain state in files, or local-only operational overrides in tenant tables.

ADR-0023 defines the Principal as the local owner of PostgreSQL and the Branch API. ADR-0029 defines bootstrap data. ADR-0031 defines secret storage. This ADR assigns configuration and data classes to their storage surfaces.

**Question:** which runtime values belong in `config.json`, the OS secure store, and PostgreSQL?

## Decision drivers

- **Secret containment** - Plaintext files must not hold credentials or private keys.
- **Installer clarity** - Operators and support need a readable non-secret runtime configuration file.
- **Domain integrity** - Business data must live in PostgreSQL with migrations, transactions, and backups.
- **Local overrides** - Branch needs local kill switches and startup settings that can take effect before database access.
- **Recoverability** - Backup and restore must capture domain data and secret material through the right mechanisms.
- **Least privilege** - Secondary devices must not store Cloud admin credentials or database credentials.

## Considered options

1. **Three-surface placement rule** - Store non-secret runtime settings in `config.json`, secret material in the OS secure store, and domain state in PostgreSQL.
2. **All configuration in `config.json`** - Store settings, secrets, and runtime state in one file.
3. **All configuration in PostgreSQL** - Store every setting and secret reference in the branch database.
4. **Environment variables only** - Configure Branch Runtime only through process environment.

## Decision outcome

**Chosen option:** _Three-surface placement rule_, because each storage surface should carry the data class that matches its security and operational properties.

### Placement table

| Storage surface | Store here                                                                                                                                  | Do not store here                                                                                         |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `config.json`   | Runtime mode, listen URLs, discovery name, log level, local feature kill-switch overrides, last-known branch server endpoint                | Secrets, private keys, refresh tokens, database passwords, Cloud admin credentials, raw card PAN          |
| OS secure store | JWT signing key material, pairing credentials, device private keys, refresh tokens if cached on device, client certificates                 | Domain data, audit records, users, roles, feature flags, catalog, prices                                  |
| PostgreSQL      | Domain data, outbox, inbox, users, password hashes, tenant features, audit records, branch configuration received through bootstrap or sync | Device private keys, plaintext pairing credentials, raw card PAN, long-lived Cloud personal access tokens |

`config.json` is a non-secret startup file. It can exist on Principal and Secondary installs, but each role uses only the fields it needs. A Secondary may store the last-known branch server endpoint in `config.json` to reconnect after reboot, but it must not store database credentials or Cloud admin credentials.

The OS secure store holds credential material that the runtime needs to prove identity or decrypt local trust. ADR-0031 defines platform-specific secret handling and rotation.

PostgreSQL holds domain and policy state that needs transactions, migrations, audit, and sync. Bootstrap and downstream sync write branch config, users, tenant features, and audit state to PostgreSQL, not to ad hoc files.

### Values Binexus must never store

- Cloud admin credentials on a Secondary.
- Raw card PAN.
- Long-lived Cloud personal access tokens in `config.json`.
- Plaintext JWT signing keys in `config.json`.
- Device private keys in git, logs, or exported support bundles.

### Positive consequences

- Developers have a clear rule for each runtime value.
- Support can inspect non-secret startup settings without exposing credentials.
- Branch backups can separate PostgreSQL data from OS-managed secrets.
- Secondary devices remain client-only and low-privilege.
- Domain data stays under database transactions and migrations.

### Negative consequences

- Installers must configure and backup more than one storage surface.
- Runtime startup needs clear errors when the OS secure store lacks required secrets.
- Some restore flows must restore both PostgreSQL and secret material.
- Developers must avoid convenient file writes for values that belong in PostgreSQL.

### Trade-offs accepted

- Binexus accepts multi-surface configuration to avoid plaintext secret storage.
- Binexus accepts startup complexity to keep domain data transactional.
- Binexus accepts separate backup paths for database state and credential material.

## Pros and cons of the options

### Option 1 - Three-surface placement rule

- **Good:** Matches each data class to a suitable storage surface.
- **Good:** Keeps `config.json` readable and non-secret.
- **Good:** Keeps domain state in PostgreSQL.
- **Good:** Gives secrets OS-level protection.
- **Bad:** Requires installer and restore coordination across file, database, and secure store.
- **Bad:** Runtime diagnostics need to report missing values without leaking secrets.

### Option 2 - All configuration in `config.json`

- **Good:** Easy to edit, back up, and inspect.
- **Bad:** Encourages plaintext secrets.
- **Bad:** Weak fit for domain data that needs transactions and audit.
- **Bad:** Makes accidental git or support-bundle leakage more likely.

### Option 3 - All configuration in PostgreSQL

- **Good:** Centralizes state and uses existing backup tooling.
- **Bad:** Startup values needed before database access have no home.
- **Bad:** Secret values still need encryption and key management outside PostgreSQL.
- **Bad:** Secondary devices should not need a local domain database.

### Option 4 - Environment variables only

- **Good:** Familiar for cloud deployment and containerized services.
- **Bad:** Poor fit for Windows installer UX and local support.
- **Bad:** Does not provide durable secure storage for device keys and certificates.
- **Bad:** Harder for non-technical branch operators to inspect safely.

## Validation

This decision is working if:

- `config.json` contains only non-secret startup settings and local overrides.
- JWT signing key material, pairing credentials, device private keys, refresh tokens, and client certificates live in the OS secure store.
- PostgreSQL holds domain data, outbox, inbox, users, password hashes, tenant features, and audit records.
- Secondary devices never receive database credentials or Cloud admin credentials.
- Support bundles can include `config.json` after an automated secret scan.

It is failing if:

- A secret appears in `config.json`.
- A Branch stores raw card PAN.
- A Secondary stores Cloud admin credentials.
- Domain data lives in ad hoc local files outside PostgreSQL.

## More information

- Related ADRs: [ADR-0005](0005-multi-tenant-shared-database.md), [ADR-0006](0006-authentication-jwt-argon2-rbac.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0023](0023-branch-installation.md), [ADR-0029](0029-bootstrap.md), [ADR-0031](0031-secrets-storage.md)
