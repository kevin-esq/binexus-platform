# Branch Runtime implementation roadmap

Sequencing for Branch Runtime and Desktop Tauri after [CHECKPOINT FINAL](../migration/branch-runtime-architecture-checkpoint.md) direction approval.

ADRs 0016–0032 stay `Proposed` until implemented or ADR policy accepts them. Each implementation PR should reference the ADRs it realizes.

Do not start PR 1 until the architecture documentation PR is merged to `main`.

PR 1 merged (`#75`). PR 2 lives on `feat/branch-server-identity`. Checkpoint: [`pr2-branch-server-identity-checkpoint.md`](../migration/pr2-branch-server-identity-checkpoint.md).

## PR plan (single developer)

| PR  | Title                                   | Demonstrable value                                                                                                | Depends on | Tests                   | Risks                  | Done when                            | Out of scope            |
| --- | --------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ---------- | ----------------------- | ---------------------- | ------------------------------------ | ----------------------- |
| 1   | Runtime mode foundation                 | `AddBinexusCore` + `AddCloudRuntime` / `AddBranchRuntime` stubs; architecture test forbids mode checks in Modules | `main`     | DI / architecture tests | Mode leaks into domain | Branch boots health; Cloud unchanged | Sync, Tauri, installer  |
| 2   | Branch Server identity and health       | `BranchInstance` config + `/health` with instance metadata                                                        | PR1        | API tests               | Wrong listen bind      | Health shows instance and mode       | Pairing, sync           |
| 3   | Desktop Tauri shell and server profiles | App opens, stores Branch URL profile, calls health                                                                | PR2        | Smoke UI                | Secret mishandling     | Connects to Branch health            | POS, pairing            |
| 4   | Device pairing                          | Pair Branch Client ↔ Branch Server with device credential                                                         | PR2–3      | Pairing integration     | Weak approval UX       | Paired device required for API       | Cloud activation polish |
| 5   | POS flow against Branch                 | `CreateSale` commits on Branch Server                                                                             | PR4        | Sales tests + e2e       | Session invariants     | Sale confirmed without Cloud         | Sync upstream           |
| 6   | Multi-terminal LAN validation           | Two clients, two terminals, sessions                                                                              | PR5        | Concurrency             | Session collisions     | Two cajas operate                    | Installer               |
| 7   | Installer spike                         | Elevated install of Postgres + Windows Services                                                                   | PR2        | Manual checklist        | Elevation failures     | Services survive reboot              | Wizard polish           |
| 8   | Sync Journal upstream foundation        | Journal + worker skeleton + checkpoint                                                                            | PR1–2      | Worker tests            | Conflating Outbox      | Journal rows after local event       | Full sales sync         |
| 9   | Sync upstream Sales                     | Sale reaches Cloud idempotently                                                                                   | PR8, PR5   | Integration             | Duplicates             | Cloud shows sale after sync          | Downstream catalog      |
| 10  | Downstream configuration                | Flags / users / config pull                                                                                       | PR8        | Integration             | Stale auth             | Branch applies config version        | Full catalog product    |

## Later PRs

Bootstrap UX, proof object pipeline, backup scheduler, version gates, Web Admin freshness fields, catalog downstream, Branch Server activation polish.

## Rules

- One concern per PR.
- No mega-branch with Runtime + Wizard + Tauri + Installer + Sync.
- New work from updated `main` on a new branch each time.
