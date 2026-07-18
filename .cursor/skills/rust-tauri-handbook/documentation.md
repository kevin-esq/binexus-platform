# Documentation Standards

## Rustdoc

- Public items in library crates: `///` docs with at least one sentence of intent
- `# Errors` / `# Panics` / `# Safety` sections when relevant
- Examples for non-obvious parsers and policy types
- `cargo doc --workspace --no-deps` should build clean

## Capability docs

Every capability JSON `description` states **why** those permissions exist. PR that widens permissions must explain blast radius.

## ADRs

Use repo ADR process (`docs/adr/`) for:

- Offline sync model changes
- Secret storage backends
- Updater / signing
- New privileged plugins (shell, fs broad scopes)
- Trust-boundary changes (remote capabilities, isolation pattern)

## Module READMEs

Optional short `README.md` inside a feature crate when onboarding cost is high. Prefer rustdoc + handbook links over duplicate prose.

## IPC contract

Document command names, input/output shapes, and error codes next to the TypeScript bindings (or generate types). Stable error codes are part of the public API.

## Changelog

User-visible desktop changes: note capability, updater, or pairing impacts explicitly.
