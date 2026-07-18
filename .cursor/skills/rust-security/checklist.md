# Security checklist (Rust + Tauri)

## Before merge

- [ ] Capability diff reviewed; least privilege
- [ ] CSP updated if new origins
- [ ] No secrets in IPC responses or logs
- [ ] Command authz/policy enforced in Rust when relevant
- [ ] Plugin scopes (path/host) allowlist-only
- [ ] `unsafe` count unchanged or justified
- [ ] `cargo audit` / `cargo deny check` pass
- [ ] Frontend cannot reach privileged APIs without capability

## Threats to remember

| Threat         | Mitigation                                        |
| -------------- | ------------------------------------------------- |
| XSS → IPC      | Capabilities + CSP + isolation pattern (optional) |
| Path traversal | Scoped fs + canonicalize checks                   |
| Supply chain   | audit/deny, pin lockfile, review build scripts    |
| Secret theft   | keyring; no plaintext at rest                     |
| Updater MITM   | HTTPS + signature verification                    |
| Malicious Rust | Code review — capabilities cannot save you        |
