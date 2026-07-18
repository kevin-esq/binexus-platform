# CI/CD and Deployment Standards

## CI pipeline (required gates)

| Job                  | Command / action                                        |
| -------------------- | ------------------------------------------------------- |
| Format               | `cargo fmt --check`                                     |
| Lint                 | `cargo clippy --workspace --all-targets -- -D warnings` |
| Test                 | `cargo test --workspace`                                |
| Audit                | `cargo audit` (fail on vulns; ignores documented)       |
| Deny                 | `cargo deny check`                                      |
| Frontend             | existing web lint/test for UI package                   |
| E2E (optional stage) | WebDriver on Linux (`xvfb-run`) + Windows               |

Run audit on schedule (daily/weekly) — advisories appear without code changes.

## Suggested GitHub Actions shape

1. `setup-rust` + cache (`Swatinem/rust-cache`)
2. Install Linux WebKit deps when building Tauri on Ubuntu
3. Parallel: rust-check | frontend-check
4. `tauri build` on tagged releases per OS matrix
5. Upload signed artifacts; publish updater JSON/manifest

Official references: [Tauri WebDriver CI](https://v2.tauri.app/develop/tests/webdriver/ci/), updater plugin docs.

## Bundling

- Configure targets in `tauri.conf.json` (`bundle.targets`)
- Icons for all platforms
- Windows: code signing cert in CI secrets
- macOS: notarization when distributing outside direct enterprise deploy

## Updater

- Generate keypair via Tauri CLI; store **private** key in CI secrets only
- Set `createUpdaterArtifacts: true`
- Embed **public** key in config
- **Never** enable `dangerousInsecureTransport` in production
- Verify signature before install; relaunch via process plugin

## Environments

| Env     | Behavior                                                 |
| ------- | -------------------------------------------------------- |
| Dev     | Loose logging; optional insecure store backend for tests |
| Staging | Production-like capabilities; staging updater endpoint   |
| Prod    | Minimal capabilities; keyring secrets; audit-clean deps  |

## Rollback

- Keep N previous installer versions downloadable
- Updater endpoint can pin/serve last-known-good
- Document recovery for corrupted local config (Binexus: `RecoveryRequired`)
