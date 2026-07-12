# CHECKPOINT FRONTEND SWITCH — FINAL

Date: 2026-07-12  
Status: **CLOSED** — real cutover demonstrated. NestJS later deleted in Gate 7 ([ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md)).

Matrix: [`frontend-switch-matrix.md`](./frontend-switch-matrix.md)  
Nest audit: [`nest-dependency-audit.md`](./nest-dependency-audit.md)  
MinIO: [`../../infra/minio/README.md`](../../infra/minio/README.md)

---

## Execution proof (mandatory)

```text
$env:Jwt__SigningKey = "local-build-signing-key-with-more-than-thirty-two-bytes"
pwsh -File backend/scripts/gate5-smoke-stack.ps1
→ GATE5 STACK PASS
```

Stack: Docker Postgres → EF migrate → Binexus.Api + Workers → readiness → `SMOKE_REQUIRE=1` → Playwright (Next.js :3000) → teardown.

| Gate                     | Result                                    |
| ------------------------ | ----------------------------------------- |
| API .NET really up       | Yes (`/health` on `:5102`)                |
| Web really up            | Yes (Playwright `webServer` Next `:3000`) |
| `SMOKE_REQUIRE=1`        | **PASS** (no skip)                        |
| Required tests skipped   | **0**                                     |
| Requests to Nest `:3001` | **0** (asserted in e2e)                   |

### Smoke functional (`SMOKE_REQUIRE=1`)

```text
OK health / login / me / refresh / me / logout / old refresh rejected
OK adjust stock / list stock
OK create order → approve → outbox PICKING → complete → READY_FOR_DELIVERY_ROUTE
OK logistics candidate → route → assign → dispatch → proof PUT → confirm → liquidate
OK POS open → sale CASH+CARD → close (declared=expected=400, after=CLOSED)
SMOKE PASS
```

Log: `artifacts/gate5-smoke/smoke.log`

### Browser (Playwright)

```text
1 passed (gate5-smoke.spec.ts)
- login UI → /orders
- navigate /orders /inventory /warehouse /logistics /pos
- pages render; no Nest :3001; API hits :5102; no 5xx
```

---

## Checklist vs requirements

| Item                                      | Status                                                   |
| ----------------------------------------- | -------------------------------------------------------- |
| Smoke with `SMOKE_REQUIRE=1` + real stack | Done                                                     |
| Functional flow per module                | Done                                                     |
| Minimal browser test                      | Done (1/1)                                               |
| Refresh single-flight                     | SDK + tests (9/9 SDK suite)                              |
| Token storage + logout clear              | `localStorage`; documented XSS; no cookies change        |
| No executable Nest dependency             | Audit + e2e assert                                       |
| Idempotency key reused on 401 retry       | SDK                                                      |
| Dev object storage hardened               | Issued intents, Prod 404, tests                          |
| MinIO CORS reproducible                   | `infra/minio/` + Testcontainers MinIO                    |
| Seeds by environment                      | Dev enables POS/LIQUIDATION; Testing off; Prod no seeder |
| UI Problem Details mapping                | `error-messages.ts` Spanish actionable                   |
| Mutating OpenAPI schemas                  | Orders create/mutate typed; regen SDK                    |
| Clean/reproducible frontend build         | typecheck/lint/build OK                                  |
| Backend tests                             | **233/233** (or current total after Gate 5 hardening)    |
| E2E passed/failed/skipped                 | **1 / 0 / 0**                                            |

---

## Token storage

|         |                                             |
| ------- | ------------------------------------------- |
| Access  | `localStorage['binexus.accessToken']`       |
| Refresh | `localStorage['binexus.refreshToken']`      |
| Risk    | XSS can read tokens — accepted for Gate 5   |
| Future  | Tauri OS-secure storage via `TokenProvider` |
| Cookies | Not in Gate 5                               |

## Auth / refresh

- Single-flight refresh on 401; concurrent waiters share one refresh.
- Failed refresh → `clear()` tokens.
- `403 FEATURE_DISABLED` does not trigger refresh.
- Logout clears both tokens; old refresh rejected (smoke).

## Idempotency

- Key generated once per logical SDK `request()`; reused on the single 401 retry.
- New user action → new key.
- Higher-level network retries should pass a stable `idempotencyKey`.

## Dev object storage

- Dev/Testing only; Production/Staging not mapped (404).
- Excluded from public OpenAPI.
- Issued key + MIME + size; no overwrite; no traversal.

## Frontend verify

```text
pnpm --filter @binexus/sdk test       → 9/9
pnpm --filter @binexus/sdk typecheck  → OK
pnpm --filter @binexus/web typecheck  → OK
pnpm --filter @binexus/web lint       → OK
pnpm --filter @binexus/web build      → OK
```

## Backend verify

```text
dotnet test -c Release → 233/233 (Arch 29 + Unit 58 + Integration 146; 0 failed / 0 skipped)
```

## Risks remaining

1. Dual Nest/.NET in monorepo until Gate 7 — wrong `NEXT_PUBLIC_API_URL` can still hit Nest.
2. localStorage XSS.
3. Playwright suite is minimal (happy path navigation), not full visual coverage.
4. CI Gate 5 stack job wired in Gate 6 as `dotnet-smoke` (compose + smoke). See [`gate6-checkpoint.md`](./gate6-checkpoint.md).
5. UUID clean DB only — no Nest cuid migration.

## Nest deletion readiness

**Completed in Gate 7.** See [`gate7-checkpoint.md`](./gate7-checkpoint.md) and [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md).
