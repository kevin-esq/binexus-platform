# CHECKPOINT — BRANCH DEVICE AUTHENTICATION FINAL PRE-PUSH

**Status:** Blockers closed. Local commits rewritten. **No push / PR / merge.**  
**Branch:** `feat/branch-device-auth`  
**HEAD:** `476c92f`  
**Base:** `origin/main` @ `2c95236`

---

## 1. `dotnet format` exit 2 — cause and resolution

| item                                      | detail                                                                                                                                                                                     |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **command (official gate)**               | `dotnet format apps/backend/Binexus.slnx --verify-no-changes`                                                                                                                              |
| **exit code**                             | **0** (after fix)                                                                                                                                                                          |
| **files reported (before)**               | Pairing-era `.cs` under Platform/tests (e.g. `BranchDevicePairingService.cs`, …) — **outside DeviceAuth content diff**; after CRLF checkout also DeviceAuth paths as EOL-only              |
| **in diff?**                              | Content: no. Git blobs already LF. Worktree had CRLF via `core.autocrlf=true`                                                                                                              |
| **rule**                                  | `.editorconfig` → `end_of_line = lf`                                                                                                                                                       |
| **expected EOL**                          | LF in working tree and blobs                                                                                                                                                               |
| **`core.autocrlf`**                       | `true` locally → checkout smudges LF blobs to CRLF → format fails                                                                                                                          |
| **clean LF checkout**                     | `git -c core.autocrlf=false worktree add` → format **exit 0** (same as Linux CI)                                                                                                           |
| **commits contain involuntary EOL?**      | No — blobs remain LF-only                                                                                                                                                                  |
| **`git diff --check origin/main...HEAD`** | exit **0**                                                                                                                                                                                 |
| **fix**                                   | Added `.gitattributes` (`* text=auto eol=lf`) in final docs commit so Windows worktrees stay LF; primary worktree format now **exit 0**. No mass content renormalize of unrelated history. |

## 2. DAT binding to paired `BranchInstanceId` after restart

`ensure_access_token(client, identity: &DeviceAuthIdentity)` takes explicit:

- `branch_instance_id`
- `device_id`
- `terminal_id: Option<Uuid>`
- `branch_base_url`

from `DesktopConfig` (via `ensure_device_session` / interop harness), **not** from RAM DAT.

Before sign: `challenge.branch_instance_id == identity.branch_instance_id`.  
Mismatch → clear DAT → `BranchIdentityMismatch` → no auto-retry.

## 3. DeviceId / token validation

After token:

- `token.branch_instance_id == identity.branch_instance_id`
- `token.device_id == envelope.device_id == identity.device_id`
- if `identity.terminal_id` set: `token.terminal_id` must match; else mismatch (no silent rebind)

Also rejects client URL ≠ identity `branch_base_url`.

## 4. Structural single-flight

`AsyncMutex<FlightSlot>` + `watch` channel:

- idle→running creates shared flight under one lock
- followers await same result
- N callers → one ceremony → same success/failure
- new call after completed failure may retry
- `clear` bumps cancellation epoch / ends waiters

## 5. Deterministic concurrent tests

In commit 7 (`session.rs` `#[cfg(test)]`):

- `single_flight_ten_callers_share_one_challenge_and_token` (Barrier)
- `single_flight_ten_callers_share_leader_failure_without_extra_flight`
- `new_call_after_failed_ten_caller_flight_can_retry`
- `clear_ends_all_single_flight_waiters`
- binding: restart/foreign challenge, foreign token, deviceId, terminalId, wrong URL, inconsistent token

## 6. DAT zeroization

- `DatMaterial.access_token: Zeroizing<String>` (no `Debug`)
- bearer returns `Zeroizing<String>`
- dep: `zeroize = "=1.9.0"`
- structural test: `dat_material_access_token_is_zeroizing_string`

## 7. Corrected commit history

| #   | Hash                                       | Subject                                                            |
| --- | ------------------------------------------ | ------------------------------------------------------------------ |
| 1   | `3d5e9e40dd3edd12c21605b7a4776aa569a9a747` | docs(branch-device-auth): define operational device authentication |
| 2   | `6c9590a02281f19155f53acec8abe12737d18b60` | feat(platform): add Branch device authentication services          |
| 3   | `eef419a46a1583d8a0d0a7bb4e132fe2536e0b5d` | feat(api): expose Branch device authentication endpoints           |
| 4   | `3d45ea7401b6eed491b0cc5d32b238a56094ac81` | feat(modules): enforce device and user auth on Branch operations   |
| 5   | `c39a3faa98aaac4f1199ccff0860c22cb3a1de48` | feat(desktop): add Branch device access token lifecycle            |
| 6   | `8946bd38dca520cfda79cfd761aacf0a9525c353` | test(backend): cover Branch device authentication security         |
| 7   | `189713eebf207b1667044a3cf774e13313b33b92` | test(desktop): cover DAT lifecycle and single-flight               |
| 8   | `476c92f73fa5791e054c9b961d9eb4e6b0071278` | docs(branch-device-auth): document implementation checkpoint       |

- Commit 5: desktop impl **without** `#[cfg(test)]`
- Commit 7: tests module + golden fixture (+1088 lines)
- Commit 8: checkpoints + `.gitattributes`
- No `ci(...)` commit (workflow unchanged)

## 8. `git status --short`

Clean after this checkpoint was added to the final docs commit. `CHECKPOINT-COMMITS-READY.md` was superseded and removed (obsolete pre-rewrite hashes).

## 9. `git log --oneline --decorate -12`

```text
476c92f (HEAD -> feat/branch-device-auth) docs(branch-device-auth): document implementation checkpoint
189713e test(desktop): cover DAT lifecycle and single-flight
8946bd3 test(backend): cover Branch device authentication security
c39a3fa feat(desktop): add Branch device access token lifecycle
3d45ea7 feat(modules): enforce device and user auth on Branch operations
eef419a feat(api): expose Branch device authentication endpoints
6c9590a feat(platform): add Branch device authentication services
3d5e9e4 docs(branch-device-auth): define operational device authentication
2c95236 (origin/main) docs: close numbered migration; adopt product initiatives (#82)
```

## 10. Full regression

| gate                                   | exit                        |
| -------------------------------------- | --------------------------- |
| `dotnet build`                         | 0                           |
| `dotnet test` (full slnx)              | 0 (441 tests: 244+143+48+6) |
| `dotnet format --verify-no-changes`    | **0**                       |
| `git diff --check origin/main...HEAD`  | **0**                       |
| EF `has-pending-model-changes`         | 0                           |
| `cargo fmt --check`                    | 0                           |
| `cargo clippy … -D warnings`           | 0                           |
| `cargo test --workspace --all-targets` | 0                           |
| `pnpm install --frozen-lockfile`       | 0                           |
| `pnpm --filter @binexus/desktop test`  | 0                           |
| `pnpm --filter @binexus/desktop build` | 0                           |

## 11. Interop

`DeviceAuthRustProductInteropTests` → **passed** (exit 0). Uses updated `device_auth_interop` with `DeviceAuthIdentity` from paired config.

## 12. Branch OpenAPI

Artifact in commit 3; contract tests green; contains `DeviceBearer`.

## 13. Cloud OpenAPI

`artifacts/openapi/binexus-v1.json` **not** in commits; no `DeviceBearer` / `device-auth`; restored after Api OpenAPI-on-build.

## 14. Audits

| audit                                                    | result                                                                                                                                   |
| -------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet High/Critical (`Binexus.Api`)                      | none                                                                                                                                     |
| `cargo audit` raw                                        | exit **1** (do not call PASS)                                                                                                            |
| `cargo audit` + exceptions `RUSTSEC-2026-0194/0195/0009` | exit **0**; exceptions **unchanged** (still carry dependencyPath, binexusImpact, mitigation, exceptionOwner, reviewBy, removalCondition) |

## 15. Diff vs `origin/main`

8 commits · **79 files** · +11650 / −26.

## 16. Remaining blockers / risks

1. HMAC DAT key can mint tokens (known PLAN limit).
2. LAN TLS / pinning / user session / Offline Sales still out of scope.
3. cargo-audit High transitive via Tauri — policy exceptions only (not expanded).
4. Local untracked checkpoint markdowns only — not part of push until you choose.

---

**Stop.** No `git push`, `gh pr create`, or merge.
