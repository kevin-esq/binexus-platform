# Branch Runtime implementation roadmap

> **Status:** The numbered migration **PR1–PR5 is complete and merged**. This document is a **historical** sequence. Do not plan new work as “PR 6 / PR 7 / PR X.Y”. Reframe remaining backlog as **named product initiatives** (e.g. Offline Sales Engine, Branch Device Auth) with `feat/<capability-name>` branches and capability-titled plans/checkpoints. New docs do not go in `docs/migration/`.

Sequencing for Branch Runtime and Desktop Tauri after [CHECKPOINT FINAL](../migration/branch-runtime-architecture-checkpoint.md) direction approval.

ADRs 0016–0032 stay `Proposed` until implemented or ADR policy accepts them. Each implementation PR should reference the ADRs it realizes.

## Historical PR plan (migration; PR1–PR5 done)

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

## Later (reframe as named initiatives)

Former “later PRs” backlog — plan each as a capability, not a migration index: bootstrap UX, proof object pipeline, backup scheduler, version gates, Web Admin freshness fields, catalog downstream, production OS credential store; plus anything still listed in the table above that was never started under PR1–PR5 (POS/offline sales, multi-terminal LAN, installer, sync journal, etc.).

## Rules

- One concern per product PR / branch.
- No mega-branch with Runtime + Wizard + Tauri + Installer + Sync.
- New work from updated `main` on `feat/<capability-name>`.
- Name the initiative before planning; checkpoints use the capability name.
