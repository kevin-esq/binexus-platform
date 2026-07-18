# Development Workflows

## Feature loop

1. **Spec the trust boundary** — what must stay in Rust vs UI.
2. **Write failing tests** (domain unit → command integration → optional WebDriver).
3. **Implement domain / service** without Tauri types when possible.
4. **Add `#[tauri::command]` adapter** — validate input, map errors to public codes, register in `generate_handler!`.
5. **Grant capability permission** only for the windows that need it; update CSP if assets change.
6. **`cargo clippy --all-targets -- -D warnings`**, `cargo test`, frontend typecheck.
7. **Manual smoke** on target OS (Windows first for Binexus Branch Client).

## Adding a Tauri plugin

1. `cargo add tauri-plugin-*` + npm `@tauri-apps/plugin-*`
2. `.plugin(...)` in Builder
3. Add permission strings to the correct capability file
4. Verify runtime (missing permission fails at invoke, not compile)

## Dependency change

1. Prefer `[workspace.dependencies]` version
2. `cargo update -p <crate>` deliberately
3. `cargo audit` / `cargo deny check`
4. Note risk in PR (build.rs / proc-macro / `unsafe`)

## Refactor rules

- Extract crate when compile times hurt or ownership splits
- Do not introduce circular crate deps — extract `contracts`
- Keep commands thin; move logic out of `commands/`

## Code review gate

Use [`rust-code-review`](../rust-code-review/SKILL.md). Block merge on: secrets in frontend, capability widening without justification, `unwrap` in command paths, missing tests for money/pairing/crypto.

## Release loop

See [cicd-deployment.md](cicd-deployment.md). Sign updater artifacts; never ship `dangerousInsecureTransport`.
