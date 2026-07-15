# CHECKPOINT PR 1 — RUNTIME MODE FOUNDATION

**Status:** Implementation complete (uncommitted on `feat/branch-runtime-foundation`)  
**Date:** 2026-07-12  
**Scope:** .NET composition foundation for Cloud / Branch hosts. No Tauri, sync, pairing, activation, installer, certificates, mDNS, or hardware.

---

## Design final — RuntimeMode

```csharp
// Platform only — not SharedKernel, not Modules
public enum RuntimeMode { Cloud, Branch }

public sealed class BinexusRuntimeOptions
{
    public const string SectionName = "Binexus";
    public string? RuntimeMode { get; init; } // null = absent
}

public interface IRuntimeDescriptor
{
    RuntimeMode Mode { get; }
}
```

### Absence detection

Options bind `string? RuntimeMode`. Composition reads `configuration["Binexus:RuntimeMode"]` and calls `RuntimeModeParser.ParseRequired`. A missing or empty value never becomes `Cloud` via enum zero.

### Parse rules (`RuntimeModeParser`)

| Input                       | Result                |
| --------------------------- | --------------------- |
| null / section key absent   | fail                  |
| `""` / whitespace-only      | fail                  |
| `Cloud` / `cloud` / `CLOUD` | Cloud                 |
| `Branch` / `branch`         | Branch                |
| `" Cloud "` (trim)          | **accepted** as Cloud |
| unknown (`Local`, `Nope`)   | fail                  |
| internal spaces (`Cl oud`)  | fail                  |

Errors name `Binexus:RuntimeMode` and allowed values. No secrets in the message.

Single composition entry for Api and Workers:

```csharp
services.AddBinexusRuntime(configuration);
```

No `BuildServiceProvider()` during registration. No duplicated switch in both `Program.cs` files.

OpenAPI GetDocument host: controlled Cloud only when the entry assembly / args indicate document generation. Not an image default and not a production fallback.

---

## Responsibilities

### `AddBinexusCore` (`Binexus.Composition`)

- Platform persistence / tenancy / shared platform services
- Dispatching
- Modules: Identity, Inventory, Orders, Warehouse, Logistics, Sales
- Outbox/inbox **services** used by processors (not the Workers hosted service)

Does **not** register: Problem Details, JWT middleware pipeline, CORS, rate limiting, OpenAPI, business HTTP endpoints, `OutboxWorkerHost`.

### Api host

- Serilog / forwarded headers (as before)
- `AddBinexusCore` → `AddBinexusRuntime`
- Problem Details, Authentication/JWT, Authorization, CORS, rate limiting, OpenAPI, HTTP health, module endpoints
- `MapRuntimeHealth()` → `GET /health/runtime`

### Workers host (`WorkersHost`)

- `AddBinexusCore` → `AddBinexusRuntime`
- `AddHostedService<OutboxWorkerHost>` (Program only)
- Operational maps: `/health`, `/health/live`, `/health/runtime`
- No MVC business endpoints, OpenAPI, or CORS

### `AddCloudRuntime` / `AddBranchRuntime`

PR 1: each registers exactly one `IRuntimeDescriptor` (`CloudRuntimeDescriptor` / `BranchRuntimeDescriptor`). No BranchId, sync, devices, or certs yet.

---

## Capability matrix (PR 1)

| Area                                                                  | Cloud           | Branch          |
| --------------------------------------------------------------------- | --------------- | --------------- |
| Identity / Inventory / Orders / Warehouse / Logistics / Sales modules | shared          | shared          |
| Current HTTP endpoints                                                | shared (compat) | shared (compat) |
| `OutboxWorkerHost`                                                    | Workers only    | Workers only    |
| Runtime descriptor                                                    | Cloud           | Branch          |

Later PRs will mark Cloud-only / Branch-only endpoints when real capabilities exist. No fake markers in PR 1.

`Cloud` in Compose/CI/dev is **PR 1 compatibility**, not a claim that every current operation stays Cloud forever.

---

## Hosted services matrix

| Hosted service     | Api Cloud | Api Branch | Workers Cloud | Workers Branch |
| ------------------ | --------- | ---------- | ------------- | -------------- |
| `OutboxWorkerHost` | no        | no         | once          | once           |

---

## Configuration surfaces

| Surface                                                    | Mode set?                                 |
| ---------------------------------------------------------- | ----------------------------------------- |
| Docker **final** image                                     | **no** (neutral)                          |
| Docker **build** stage                                     | Cloud (efbundle / tooling only)           |
| Compose `x-dotnet-env`                                     | `Binexus__RuntimeMode: Cloud`             |
| `.env.example`                                             | `Binexus__RuntimeMode=Cloud`              |
| `launchSettings.json` Api/Workers                          | Cloud                                     |
| `appsettings.Development.json` Api/Workers                 | Cloud (local DX)                          |
| CI backend Build / EF / Test steps                         | Cloud                                     |
| Integration fixtures (`CloudApiFactory`, Postgres fixture) | Cloud explicit                            |
| Missing/invalid tests                                      | clear process env; no shared silent Cloud |

---

## Endpoint

`GET /health/runtime` → `{ "runtimeMode": "Cloud" | "Branch" }`

- Api + Workers
- `.ExcludeFromDescription()` (not in OpenAPI artifact)
- No DB, no secrets, no hostname/env/connection strings

---

## Tests

| Suite                             | Coverage                                                                                                         |
| --------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Unit `RuntimeModeParserTests`     | trim, casing, null, empty, unknown                                                                               |
| Integration `ApiRuntimeHostTests` | Cloud/Branch start, single descriptor, no OutboxWorkerHost, missing/invalid                                      |
| Workers `Binexus.Workers.Tests`   | real host via `WorkersHost`, Cloud/Branch, Outbox once, missing/invalid, cancel                                  |
| Architecture                      | modules ↛ Platform.Runtime; Platform ↛ Composition/Modules; final Dockerfile stage has no `Binexus__RuntimeMode` |

---

## Verification (2026-07-12)

| Check                                 | Result                                                                 |
| ------------------------------------- | ---------------------------------------------------------------------- |
| `dotnet test Binexus.slnx -c Release` | Unit 72, Architecture 33, Workers.Tests 6, Integration 154 — all green |
| OpenAPI contract                      | `/health/runtime` absent; artifact left unchanged vs main              |
| EF `has-pending-model-changes`        | no pending                                                             |
| EF migrations added                   | **zero**                                                               |
| NuGet vulnerable list                 | no severity hits in audit spot-check                                   |
| SDK packages                          | no intentional contract change                                         |

---

## ADR-0016

Status stays **Proposed**. Implementation note documents PR 1 runtime composition only. Branch Client / Tauri not claimed.

---

## Risks / follow-ups

- Cloud and Branch compositions are still identical beyond the descriptor; surface split comes later.
- OpenAPI GetDocument special-case must stay narrow; prefer CI env `Binexus__RuntimeMode=Cloud` for builds.
- Process env can leak into “missing mode” tests; tests already clear `Binexus__RuntimeMode`.
- Next slice: PR 2 Branch identity & health (new branch from updated `main` after merge).
