---
name: rust-code-review
description: Code review rubric for Rust and Tauri PRs — security, architecture, IPC, tests, dependencies. Use when reviewing pull requests that touch apps/desktop, src-tauri, or Rust crates, or when the user asks for a Rust/Tauri review.
---

# rust-code-review

## Severity labels

- **Blocker** — must fix before merge
- **Major** — should fix in this PR
- **Nit** — optional

## Blockers

- Secrets in frontend, logs, or repo
- Capability widening without justification
- `unwrap` in command/crypto/pairing paths
- Missing tests for security-sensitive changes
- SQL concatenation / unscope fs/shell
- Circular crate deps / domain depending on Tauri

## Majors

- Sync I/O on async/UI path
- Oversized IPC payloads
- Public errors leaking internals
- New deps without audit note
- Dead code / unused broad tokio features

## Review order

1. Trust boundary & capabilities
2. Error & secret handling
3. Correctness & tests
4. Architecture boundaries
5. Performance / DX nits

## Output format

```text
## Verdict: approve | request changes
## Blockers
- ...
## Majors
- ...
## Nits
- ...
```

Use handbook [anti-patterns](../rust-tauri-handbook/anti-patterns.md) and [checklists](../rust-tauri-handbook/checklists.md).
