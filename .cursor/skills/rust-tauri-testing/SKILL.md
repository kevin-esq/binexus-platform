---
name: rust-tauri-testing
description: Testing standards for Rust and Tauri — unit, integration, wiremock, WebDriver/E2E, coverage expectations, CI test jobs. Use when writing Rust tests, Tauri command tests, desktop E2E, or setting up tauri-driver/WebdriverIO.
---

# rust-tauri-testing

## Pyramid

| Layer       | What                                       | Tools                                                |
| ----------- | ------------------------------------------ | ---------------------------------------------------- |
| Unit        | Domain, crypto, URL policy, pure logic     | `cargo test`                                         |
| Integration | Commands, HTTP, keyring fakes, temp config | `tempfile`, `wiremock`                               |
| E2E         | Full app UI + IPC                          | WebdriverIO + `@wdio/tauri-service` / `tauri-driver` |

## Always

- Cover happy path + expected rejection + recovery for critical flows
- Use injectable stores (Binexus `memory_store` pattern) for secret tests
- Keep tests deterministic (no real network without mock)
- Run Clippy on test targets too

## Never

- Require production keyring in unit tests
- Skip pairing/url-policy/recovery cases on Branch Client
- Rely only on manual clicks for release-critical paths

## Tauri E2E notes

- Official path: https://v2.tauri.app/develop/tests/webdriver/
- Linux CI: `xvfb-run`; install `webkit2gtk` + driver packages
- macOS: prefer embedded WebDriver via WDIO Tauri service
- Mock IPC where full hardware is unavailable

## Coverage expectations

| Area                       | Bar                        |
| -------------------------- | -------------------------- |
| Crypto / pairing / secrets | High — edge cases required |
| Pure parsers/policy        | High                       |
| Thin command adapters      | Smoke + error mapping      |
| UI chrome                  | Targeted E2E, not 100%     |

## CI

`cargo test --workspace` on every PR; E2E on main/nightly or labeled PRs if runtime is heavy.
