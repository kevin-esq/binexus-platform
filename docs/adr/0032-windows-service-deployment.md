# ADR-0032: Branch Runtime Windows Service deployment

| Field    | Value                                              |
| -------- | -------------------------------------------------- |
| Status   | Proposed                                           |
| Date     | 2026-07-12                                         |
| Deciders | Kevin Esquivel                                     |
| Tags     | branch, windows-service, deployment, postgres, ops |

## Context and problem statement

The Principal Server keeps a sucursal operational when Cloud connectivity fails. It must keep the Branch API, background workers, local sync, and PostgreSQL available without depending on an interactive desktop session. Secondary cashier terminals and optional Tauri use the Principal over LAN, but they must not own the backend process lifetime.

ADR-0023 selects Principal Server plus Secondary Cashier installs. This ADR defines how the Principal runs on Windows and how Binexus updates and rolls back that local runtime.

**Question:** how should Binexus run, update, and roll back the Branch API, Workers, and PostgreSQL on a Windows Principal?

## Decision drivers

- **No interactive login dependency** - Branch operations must survive reboot without a user signing in.
- **Service recovery** - Windows should restart failed backend components and record useful logs.
- **Local database ownership** - Principal needs a dedicated PostgreSQL instance managed with the branch install.
- **Update safety** - Runtime updates need a repeatable path for binaries, migrations, and rollback.
- **Tauri separation** - Desktop client lifetime must not control Branch API lifetime.
- **Supportability** - Support needs logs, service status, backups, and clear recovery steps.

## Considered options

1. **Windows Services for Branch API and Workers with dedicated installer-managed PostgreSQL** - Principal runs backend components as services and uses a dedicated local PostgreSQL instance.
2. **Run Branch API only inside the Tauri process** - The desktop app starts and owns the backend.
3. **Require interactive user login and startup shortcuts** - Windows starts backend processes after a user logs in.
4. **Use an existing customer PostgreSQL instance by default** - Installer points Branch Runtime at any available local PostgreSQL.

## Decision outcome

**Chosen option:** _Windows Services for Branch API and Workers with dedicated installer-managed PostgreSQL_, because the Principal must stay alive after reboot and should not depend on Tauri or an operator session.

The Branch API and Branch Workers run as Windows Services on the Principal Server. Services start automatically, use Windows recovery settings for restart on failure, and write stdout and stderr to the branch log directory. Structured application logs continue through the runtime logging stack.

PostgreSQL runs as a Windows Service on the Principal. Binexus prefers a dedicated local PostgreSQL instance managed by the installer. The installer may support connecting to an existing instance only through an explicit advanced path, but the default install owns a separate instance, port, data directory, service name, backup policy, and upgrade responsibility.

Tauri can run on the Principal as an optional client, but it does not host the Branch API. Closing Tauri must not stop the Branch API, Workers, sync, backups, or PostgreSQL.

### Principal service layout

| Component      | Deployment shape                               | Startup and recovery                                                                  |
| -------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------- |
| Branch API     | Windows Service                                | Automatic start, restart on failure, stdout and stderr to logs                        |
| Branch Workers | Windows Service                                | Automatic start, restart on failure, stdout and stderr to logs                        |
| PostgreSQL     | Dedicated Windows Service managed by installer | Automatic start, data directory under installer control, backup before risky upgrades |
| Tauri client   | Optional desktop app                           | User-started or auto-launched convenience only; not required for backend uptime       |

### Update and rollback

Principal updates follow one of two approved deployment mechanics:

1. **Side-by-side runtime packages** - Installer lays down a new runtime directory, updates service pointers after validation, runs migrations, and starts the new services.
2. **Stop-service, replace binaries, migrate, start** - Installer stops Branch API and Workers, replaces binaries in place, runs migrations, and starts services.

Both paths create a database backup checkpoint before migrations that cannot roll back through ordinary backward-compatible code. Routine updates prefer backward-compatible migrations and side-by-side packages where practical.

Rollback returns service pointers or binaries to the previous runtime package and restores the matching database backup when the migration changed data in a non-reversible way. If migrations are backward compatible, rollback can use the previous binaries against the current database after validation.

### Logging and diagnostics

The service wrapper captures stdout and stderr to files under the branch log directory. Branch API and Workers also emit structured logs with service name, runtime mode, branch id, branch instance id, version, and correlation ids where applicable. PostgreSQL logs remain available through its service data directory or configured log path.

### Positive consequences

- Principal backend services start after reboot without user login.
- Branch API uptime does not depend on Tauri.
- Windows recovery settings can restart failed services.
- A dedicated PostgreSQL instance gives Binexus predictable service names, paths, ports, and backups.
- Updates and rollback have a defined binary and database path.

### Negative consequences

- Installer complexity increases for services, permissions, PostgreSQL, logs, and backup.
- Dedicated PostgreSQL consumes local resources and needs patching.
- Service account permissions require careful setup.
- Rollback becomes harder after irreversible migrations.

### Trade-offs accepted

- Binexus accepts Windows Service and installer work to keep branches operational after reboot.
- Binexus accepts a dedicated local PostgreSQL by default to reduce support ambiguity.
- Binexus accepts explicit database backup checkpoints before risky migrations.

## Pros and cons of the options

### Option 1 - Windows Services for Branch API and Workers with dedicated installer-managed PostgreSQL

- **Good:** Backend starts without interactive login.
- **Good:** Windows can restart failed services.
- **Good:** Tauri remains a client, not the backend host.
- **Good:** Dedicated PostgreSQL gives predictable operations and backups.
- **Bad:** Installer and service account setup require more work.
- **Bad:** Local database upgrades and patching become Binexus responsibilities.

### Option 2 - Run Branch API only inside the Tauri process

- **Good:** Reduces initial service installer work.
- **Bad:** Closing the desktop app stops the backend.
- **Bad:** Reboot requires user login and app launch before terminals can sell.
- **Bad:** Couples UI crashes to Branch API availability.

### Option 3 - Require interactive user login and startup shortcuts

- **Good:** Simple for prototypes.
- **Bad:** A power loss or reboot can leave the branch unable to sell until a user logs in.
- **Bad:** Windows session state becomes part of backend availability.
- **Bad:** Service recovery and logging become weaker.

### Option 4 - Use an existing customer PostgreSQL instance by default

- **Good:** Avoids installing another database when a customer already has PostgreSQL.
- **Bad:** Support cannot assume version, extensions, port, permissions, backup, or service name.
- **Bad:** Customer-level database changes can break Branch Runtime.
- **Bad:** Harder to automate rollback and restore.

## Validation

This decision is working if:

- Branch API and Workers start automatically after Principal reboot.
- Closing Tauri does not stop Branch API, Workers, sync, backups, or PostgreSQL.
- Windows records service failures and restarts services according to recovery policy.
- The default installer creates or manages a dedicated local PostgreSQL service.
- Updates can replace binaries, run migrations, start services, and roll back to previous binaries plus database backup when needed.

It is failing if:

- A user must log in to keep Branch API alive.
- Branch API runs only inside the Tauri process.
- Secondary cashier terminals cannot sell after reboot until somebody opens the desktop app on the Principal.
- Rollback requires manual binary copying and manual database repair.

## More information

- Related ADRs: [ADR-0023](0023-branch-installation.md), [ADR-0024](0024-local-http-api.md), [ADR-0027](0027-synchronization-architecture.md), [ADR-0029](0029-bootstrap.md), [ADR-0030](0030-configuration-storage.md), [ADR-0031](0031-secrets-storage.md)
