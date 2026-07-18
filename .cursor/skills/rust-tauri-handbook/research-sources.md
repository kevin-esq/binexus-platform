# Research Sources

Cross-referenced while authoring this handbook (2026). Prefer official docs when they conflict with blogs.

## Official

- [The Rust Book](https://doc.rust-lang.org/book/)
- [Rust API Guidelines](https://rust-lang.github.io/api-guidelines/)
- [Cargo Book — workspaces, profiles](https://doc.rust-lang.org/cargo/)
- [Tauri v2 Security](https://v2.tauri.app/security/)
- [Tauri Capabilities](https://v2.tauri.app/security/capabilities/)
- [Tauri CSP](https://v2.tauri.app/security/csp/)
- [Calling Rust from the Frontend](https://v2.tauri.app/develop/calling-rust/)
- [Tauri SQL plugin](https://v2.tauri.app/plugin/sql/)
- [Tauri Updater](https://v2.tauri.app/plugin/updater/)
- [Tauri WebDriver testing](https://v2.tauri.app/develop/tests/webdriver/)
- [RustSec / cargo-audit](https://github.com/rustsec/rustsec)

## Guidelines / ecosystem

- [Microsoft Pragmatic Rust Guidelines](https://microsoft.github.io/rust-guidelines/)
- [Microsoft Rust Training — crate architecture](https://microsoft.github.io/RustTraining/rust-patterns-book/ch15-crate-architecture-and-api-design.html)
- [Microsoft Async production patterns](https://microsoft.github.io/RustTraining/async-book/ch13-production-patterns.html)
- [Microsoft supply-chain chapter](https://microsoft.github.io/RustTraining/engineering-book/ch06-dependency-management-and-supply-chain-s.html)
- [Blessed.rs crate list](https://blessed.rs/)
- [State of the Crates 2025](https://ohadravid.github.io/posts/2024-12-state-of-the-crates/)
- [Rust Performance Book — build config](https://nnethercote.github.io/perf-book/build-configuration.html)

## Architecture / offline

- Local-first + SQLite sync patterns (CR-SQLite, column-level LWW engines)
- Production Tauri structure write-ups (capabilities-first, plugin permission triad)

## In-repo (Binexus)

- `docs/architecture/desktop-tauri.md`
- ADR-0020 pairing, ADR-0023 LAN API security, ADR-0027 config/secrets
- `apps/desktop/src-tauri` (reference implementation)
