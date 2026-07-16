# Branch Runtime implementation roadmap

Sequencing for Branch Runtime and Desktop Tauri after [CHECKPOINT FINAL](../migration/branch-runtime-architecture-checkpoint.md) direction approval.

ADRs 0016–0032 stay `Proposed` until implemented or ADR policy accepts them. Each implementation PR should reference the ADRs it realizes.

Do not start PR 1 until the architecture documentation PR is merged to `main`.

PR 1 merged (`#75`). PR 2 checkpoint: [`pr2-branch-server-identity-checkpoint.md`](../migration/pr2-branch-server-identity-checkpoint.md).  
PR 3 on `feat/branch-server-activation`: [`pr3-branch-server-activation-checkpoint.md`](../migration/pr3-branch-server-activation-checkpoint.md) + [`pr3-activation-crypto-spike.md`](../migration/pr3-activation-crypto-spike.md).

## PR plan (single developer)

| PR  | Title                                 | Demonstrable value                                                                                                | Depends on | Tests                   | Risks                  | Done when                            | Out of scope           |
| --- | ------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ---------- | ----------------------- | ---------------------- | ------------------------------------ | ---------------------- |
| 1   | Runtime mode foundation               | `AddBinexusCore` + `AddCloudRuntime` / `AddBranchRuntime` stubs; architecture test forbids mode checks in Modules | `main`     | DI / architecture tests | Mode leaks into domain | Branch boots health; Cloud unchanged | Sync, Tauri, installer |
| 2   | Branch Server identity and health     | `BranchInstance` config + `/health` with instance metadata                                                        | PR1        | API tests               | Wrong listen bind      | Health shows instance and mode       | Pairing, sync          |
| 3   | Branch Server activation (Cloud bind) | Activation code + ECDSA challenge/exchange/confirm; local Active after Cloud confirm                              | PR2        | Unit + integration      | Secret mishandling     | Cloud Active + Branch Active         | Tauri, pairing, sync   |
| 4   | Device/Terminal pairing backend       | Pair Branch Client ↔ Branch Server with device credential                                                         | PR3        | Pairing integration     | Weak approval UX       | Paired device required for API       | Tauri UI               |
| 5   | Tauri shell + pairing client          | Desktop app stores Branch URL profile, pairs, reaches Branch health/API                                           | PR4        | Smoke UI                | Secret mishandling     | Real client validates pairing        | POS                    |
| 6   | POS flow against Branch               | `CreateSale` commits on Branch Server                                                                             | PR5        | Sales tests + e2e       | Session invariants     | Sale confirmed without Cloud         | Sync upstream          |
| 7   | Multi-terminal LAN validation         | Two clients, two terminals, sessions                                                                              | PR6        | Concurrency             | Session collisions     | Two cajas operate                    | Installer              |
| 8   | Installer spike                       | Elevated install of Postgres + Windows Services                                                                   | PR2        | Manual checklist        | Elevation failures     | Services survive reboot              | Wizard polish          |
| 9   | Sync Journal upstream foundation      | Journal + worker skeleton + checkpoint                                                                            | PR1–2      | Worker tests            | Conflating Outbox      | Journal rows after local event       | Full sales sync        |
| 10  | Sync upstream Sales                   | Sale reaches Cloud idempotently                                                                                   | PR9, PR6   | Integration             | Duplicates             | Cloud shows sale after sync          | Downstream catalog     |
| 11  | Downstream configuration              | Flags / users / config pull                                                                                       | PR9        | Integration             | Stale auth             | Branch applies config version        | Full catalog product   |

## Later PRs

Bootstrap UX, proof object pipeline, backup scheduler, version gates, Web Admin freshness fields, catalog downstream, production OS credential store.

## Rules

- One concern per PR.
- No mega-branch with Runtime + Wizard + Tauri + Installer + Sync.
- New work from updated `main` on a new branch each time.
