# Checklists

## Security (every PR touching Rust/Tauri)

- [ ] No secrets in frontend, logs, IPC payloads, or repo files
- [ ] Capabilities are minimal; new permissions justified in PR
- [ ] CSP present and tightened for new external hosts
- [ ] Commands validate/parse input (`TryFrom` / typed structs)
- [ ] Public errors are stable codes — no internal paths/URLs leaked
- [ ] Filesystem/HTTP plugin scopes are path/host allowlists
- [ ] `Cargo.lock` committed; `cargo audit` clean (or documented ignore with expiry)
- [ ] `unsafe` blocks documented + justified (or zero)
- [ ] Shell/process plugins denied unless absolute need

## Performance

- [ ] Heavy commands are `async` or use `spawn_blocking`
- [ ] No large JSON blobs over IPC when binary/`Response` fits better
- [ ] Channels used for high-frequency streams (not event spam)
- [ ] Release profile reviewed for shipping builds (LTO/strip as needed)
- [ ] Startup path avoids unnecessary plugin init / sync I/O on UI thread

## Testing

- [ ] Unit tests for domain/invariants
- [ ] Integration tests for commands / HTTP / store with temp dirs + mocks
- [ ] Critical flows: happy path + rejection + recovery
- [ ] Binexus: pairing/recovery/url-policy cases covered
- [ ] CI runs `clippy -D warnings`, `test`, `audit`/`deny`

## Architecture

- [ ] Tauri types do not leak into domain crates
- [ ] No circular crate dependencies
- [ ] State for secrets/DB lives in Rust core
- [ ] Offline writes use explicit sync/outbox semantics
- [ ] Binexus: no Postgres credentials in desktop; cache non-authoritative

## Release / deploy

- [ ] Version bumped; changelog note
- [ ] Signed updater artifacts if updater enabled
- [ ] Platform installers smoke-tested (Win/macOS/Linux as supported)
- [ ] Capabilities explicitly listed in `tauri.conf.json` for prod builds
- [ ] Debug symbols / panic strategy intentional

## PR author self-check

- [ ] Intent clear in description
- [ ] Screenshots/GIFs for UI-visible changes
- [ ] Capability diff called out
- [ ] No drive-by refactors
