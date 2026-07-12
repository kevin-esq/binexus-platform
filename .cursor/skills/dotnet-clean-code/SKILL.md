---
name: dotnet-clean-code
description: Clean Code and SOLID coding standards for Binexus .NET (`apps/backend/**/*.cs`). Use when writing or reviewing C# handlers, services, middleware, DTOs, Domain/Application/Infrastructure code, naming methods/types, Result vs exceptions, interfaces, async patterns, or refactoring God services. Pairs with `dotnet-modular-monolith` and `semantic-naming`.
---

# dotnet-clean-code (Binexus)

Clean Code + SOLID for the .NET backend. Goal: match Identity middleware / Inventory contracts — small `sealed` types, explicit records, narrow ports, one-verb handlers. Not more factories or inheritance.

## Clean Code (daily)

**Names**

- Business verbs: `TryReserveForOrder`, `ApproveOrder` — not `Process` / `DoWork`.
- One term = one concept across Domain, DTO, event, endpoint, docs.
- Avoid bare framework collisions (`Route`, `Handler`, `Service`).

**Small functions**

- Guard clauses / early returns first (see login timing + fail paths).
- Extract when a method grows past ~40–50 lines with multiple business branches.
- Side effects obvious: `Clear()` in `finally`, outbox in same TX.

**Explicit data**

- `sealed record` for requests/results.
- Expected flows → `Result` / domain codes; bugs / broken invariants → exceptions.
- `IReadOnlyList`, `CancellationToken` on all I/O async.

**Comments**

- Only non-obvious _why_ (dummy Argon2 hash, “never overrides JWT”).
- Never narrate _what_; rename instead.

**Types**

- `sealed` by default; primary constructors for stable deps.
- Prefer immutability; mutate aggregates only inside a TX.

**Usings vs fully-qualified names**

- Default: `using Binexus.Modules.Orders.Contracts;` then `IOrderFulfillmentApi` — not `Binexus.Modules.Orders.Contracts.IOrderFulfillmentApi` everywhere.
- A `using` does **not** weaken module boundaries; the ProjectReference / Contracts assembly does.
- Prefer FQN only when: name collision in the same file; a one-off mention where a using adds noise; or string/meta contexts (EF `HasOne("…")`, NetArchTest namespace strings, migrations).
- Same for tests: fakes/stubs of cross-module ports use a normal `using`, not repeated FQN.

**Formatting**

- Root [`.editorconfig`](../../../.editorconfig) owns C# indent (4) and style preferences.
- Unused `using`s: `IDE0005` at warning → `dotnet format` / `dotnet format style` removes them; build also fails (`EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`).
- Requires `GenerateDocumentationFile=true` in `apps/backend/Directory.Build.props` (CS1591 suppressed via `NoWarn`).
- Local: `dotnet format apps/backend/Binexus.slnx`
- CI: `dotnet format … --verify-no-changes --severity warn`
- Do not mix giant format-only diffs into feature PRs.

## SOLID (Binexus mapping)

| Principle | Practice here                                                                        |
| --------- | ------------------------------------------------------------------------------------ |
| **S**     | One handler = one use case. Endpoints = HTTP↔command only. Middleware = one concern. |
| **O**     | New behaviour = new command/handler/event — not a growing mega-`switch`.             |
| **L**     | Almost no domain inheritance; compose + small interfaces.                            |
| **I**     | Split ports (`IInventoryReservationApi` vs `IInventoryService`). No `IEverything`.   |
| **D**     | Application owns ports; Infrastructure implements. Domain never sees EF/HTTP.        |

**Trap:** do not add one-implementation interfaces “just in case”. Interface only at a real boundary (module edge, test seam, swappable algo).

## High-ROI coding rules

1. Guard clauses first — happy path un-nested at the bottom.
2. Fail fast with actionable messages; business denial → typed codes.
3. Keep Domain pure — invariants without `DbContext`.
4. Keep Infrastructure boring — EF, JWT, hash, HTTP mapping only.
5. Idempotency explicit — `OperationKey` / conditional claim.
6. `TimeProvider` — never raw `DateTime.UtcNow` in business code.
7. Async all the way — await everything; propagate `CancellationToken`; no `.Result` / `.Wait()`.
8. Structured logs, zero secrets — typed reasons (`LoginFailedReason`), never password/token.
9. Entities equal by Id; DTOs as records.
10. Strict nullability — `Guid?` only when the domain allows it.

## Anti-patterns in this codebase

| Smell                                           | Risk                               | Fix                                             |
| ----------------------------------------------- | ---------------------------------- | ----------------------------------------------- |
| Service / AuthService ≫ ~300 lines              | SRP broken                         | Extract token issue, refresh rotation, policies |
| One interface with 8+ unrelated methods         | ISP broken                         | Split APIs (Reservation / Sale / Query)         |
| Lifecycle mega-handler with diverging branches  | Hidden coupling                    | One handler per transition when paths diverge   |
| `DbContext` in Features / endpoints             | Layer leak                         | Dispatcher + query ports only                   |
| Exceptions for “insufficient stock”             | Noisy control flow                 | `Result` / failure codes                        |
| Boolean flag soup on methods                    | Unreadable API                     | Named methods or distinct types                 |
| Repeated `Binexus.Modules.*.Contracts.Type` FQN | Noise; false “architecture” signal | File-level `using` + short name                 |

## Pre-merge checklist

- [ ] Would ops understand the name without opening the file?
- [ ] Can the business rule be unit-tested without HTTP/Postgres?
- [ ] Can a forged body `tenantId` bypass context scoping?
- [ ] Is a client retry safe (idempotent)?
- [ ] Does every new interface have more than one reason to exist?
- [ ] Cross-module types referenced via `using`, not repeated FQN (unless collision / string / EF)?

## See also

- Architecture / layers: [`../dotnet-modular-monolith/SKILL.md`](../dotnet-modular-monolith/SKILL.md)
- Naming: [`../semantic-naming/SKILL.md`](../semantic-naming/SKILL.md)
