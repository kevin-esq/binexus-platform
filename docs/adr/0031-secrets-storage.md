# ADR-0031: Branch Runtime secrets storage

| Field    | Value                                            |
| -------- | ------------------------------------------------ |
| Status   | Proposed                                         |
| Date     | 2026-07-12                                       |
| Deciders | Kevin Esquivel                                   |
| Tags     | branch, secrets, windows, dpapi, tauri, rotation |

## Context and problem statement

Branch Runtime introduces long-lived device identity, local certificates, pairing credentials, JWT signing material, and optional cached refresh tokens. Those values let a Principal or Secondary prove identity and maintain local trust. Plaintext storage would turn a copied config file, support bundle, or git mistake into a branch compromise.

ADR-0030 assigns secret material to the OS secure store. This ADR defines platform handling, rotation, revocation, and development rules for those secrets.

**Question:** how should Binexus store, rotate, and revoke secrets for Branch Server and Tauri installs?

## Decision drivers

- **OS-backed protection** - Branch secrets need platform protection beyond file permissions.
- **Windows-first deployment** - Principal and Secondary installs target Windows first.
- **Tauri compatibility** - The desktop client needs Rust-side access to OS keychain APIs.
- **Rotation** - Pairing credentials, certificates, refresh tokens, and signing keys need replacement paths.
- **Revocation** - Cloud and Branch need a way to invalidate compromised device and branch credentials.
- **Environment discipline** - Development convenience must not leak example keys into staging or production.

## Considered options

1. **Windows Credential Manager and DPAPI with Tauri keychain access** - Branch Server and Tauri use OS-backed secret APIs on Windows.
2. **Plaintext secrets in `config.json`** - Store all local secrets beside runtime settings.
3. **Secrets committed as development fixtures** - Keep example private keys and tokens in git for setup speed.
4. **PostgreSQL-only secret storage** - Store secrets in branch database tables.

## Decision outcome

**Chosen option:** _Windows Credential Manager and DPAPI with Tauri keychain access_, because Branch Runtime needs Windows-native protection and Tauri can reach OS keychain APIs through Rust.

Branch Server stores secret material in Windows Credential Manager or DPAPI-protected storage. Tauri stores client-side secret material through OS keychain APIs from Rust, using the Windows credential store on Windows. Runtime code treats the secure store as the source for:

- JWT signing key material used by Branch runtime components.
- Pairing credentials and refresh material.
- Device private keys.
- Client certificates and private key material.
- Refresh tokens if a device caches them.

`config.json` stores references, labels, or non-secret settings only. PostgreSQL may store public identifiers, certificate fingerprints, revocation records, audit, and password hashes, but it must not store plaintext device private keys or plaintext pairing credentials.

### Rotation

Binexus supports rotation for every Branch secret class:

| Secret class                                | Rotation trigger                                             | Rotation outcome                                                                                                       |
| ------------------------------------------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| Pairing credentials                         | Pairing completion, expiry, suspected exposure, admin action | Runtime replaces one-time or short-lived material and refuses replay.                                                  |
| Device private keys and client certificates | Expiry, compromise, device replacement, policy change        | Device receives or generates new material, updates trust records, and retires old material after overlap when allowed. |
| JWT signing key material                    | Scheduled policy, compromise, environment setup              | Runtime signs with the active key and accepts prior keys only during an explicit rollover window.                      |
| Cached refresh tokens                       | Expiry, logout, revocation, device loss                      | Runtime deletes cached token and requires re-authentication or re-pairing.                                             |

Rotation events write audit records and sync revocation or replacement facts through the downstream and upstream paths defined by ADR-0027.

### Revocation

Cloud owns tenant-level revocation for `DeviceId` and `BranchInstanceId`. A tenant admin can revoke a lost terminal, compromised Principal, or stale credential. Branch applies received revocations locally and rejects future handshakes, token refreshes, or sync attempts from revoked credentials.

When Cloud is unreachable, Branch follows the last received revocation state and local expiry policy. Local administrators can apply emergency revocation on the Principal when the compromised device is inside the branch trust boundary.

### Development and production rules

Development may use generated local secrets stored outside git. Staging and Production must not contain example keys, checked-in private keys, or sample signing material. The existing JWT rule already follows this direction; Branch Runtime extends the same rule to pairing credentials, device keys, client certificates, and cached refresh tokens.

Repositories must not contain real secrets. `.env.example`, documentation, and tests may show placeholder names only. Support bundles must redact secure-store values and include only fingerprints, ids, expiry, and status.

### Positive consequences

- Plaintext config files do not contain branch credential material.
- Windows installs use OS-supported secret protection.
- Tauri can store client secrets without inventing a separate file format.
- Rotation and revocation have named lifecycle paths.
- Development fixtures cannot become staging or production credentials.

### Negative consequences

- Installer, backup, and restore flows must handle OS secure store state.
- Cross-platform support later needs equivalent keychain implementations.
- Debugging credential issues needs tooling that exposes metadata without secrets.
- Key rollover requires careful overlap handling.

### Trade-offs accepted

- Binexus accepts platform-specific secret integration to avoid plaintext local credentials.
- Binexus accepts more complex restore procedures for stronger device identity protection.
- Binexus accepts strict development rules to prevent example key drift into deployed environments.

## Pros and cons of the options

### Option 1 - Windows Credential Manager and DPAPI with Tauri keychain access

- **Good:** Uses Windows-native protection for the first deployment target.
- **Good:** Keeps secrets out of `config.json` and git.
- **Good:** Works for both Branch Server and Tauri through platform APIs.
- **Good:** Supports rotation and revocation metadata without exposing raw secrets.
- **Bad:** Adds platform-specific implementation and installer work.
- **Bad:** Backup and restore need secure-store handling.

### Option 2 - Plaintext secrets in `config.json`

- **Good:** Simple to implement and inspect.
- **Bad:** A copied config file compromises the branch.
- **Bad:** Support bundles and git mistakes can leak credentials.
- **Bad:** Violates ADR-0030.

### Option 3 - Secrets committed as development fixtures

- **Good:** Speeds up early local setup.
- **Bad:** Normalizes secret leakage.
- **Bad:** Creates pressure to reuse example keys outside development.
- **Bad:** Violates staging and production key hygiene.

### Option 4 - PostgreSQL-only secret storage

- **Good:** Centralizes backup with branch data.
- **Bad:** Database compromise exposes device identity if secrets are plaintext or poorly encrypted.
- **Bad:** The runtime needs some secrets before full database access.
- **Bad:** Secondary devices should not carry a domain database just to store secrets.

## Validation

This decision is working if:

- Branch Server secrets live in Windows Credential Manager or DPAPI-protected storage.
- Tauri uses OS keychain APIs through Rust for device secrets.
- `config.json` contains no private keys, pairing credentials, refresh tokens, or client certificate private material.
- Staging and Production contain no example JWT keys, pairing credentials, device keys, or client certificates.
- Revoking a `DeviceId` or `BranchInstanceId` prevents future handshake or refresh after Branch receives the revocation.

It is failing if:

- A private key or token appears in git.
- A Branch install keeps secrets in plaintext files.
- A support bundle includes raw secrets.
- Rotation requires reinstalling the whole branch runtime.

## More information

- Related ADRs: [ADR-0006](0006-authentication-jwt-argon2-rbac.md), [ADR-0022](0022-pairing-and-handshake.md), [ADR-0023](0023-branch-installation.md), [ADR-0027](0027-synchronization-architecture.md), [ADR-0030](0030-configuration-storage.md)
