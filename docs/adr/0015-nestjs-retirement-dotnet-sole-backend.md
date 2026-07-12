# ADR-0015: NestJS retirement — .NET 10 as sole backend

| Field    | Value                                            |
| -------- | ------------------------------------------------ |
| Status   | Accepted                                         |
| Date     | 2026-07-12                                       |
| Deciders | Kevin Esquivel                                   |
| Tags     | architecture, backend, migration, nestjs, dotnet |

## Context and problem statement

Binexus ran a dual-backend period: NestJS (`apps/backend`, Prisma, optional Redis) and a .NET 10 modular monolith (`backend/`). Gates 0–6 completed parity, frontend cutover, Docker/CI, and compose smoke on .NET.

**Question:** can NestJS remain as a supported runtime, or must it be removed?

## Decision drivers

- One backend SoR for schema, OpenAPI, workers, and ops.
- Eliminate Prisma/cuid vs EF/UUIDv7 dual-schema risk.
- Shrink CI, compose, and dependency surface.
- Preserve ADRs and migration docs as history (do not rewrite the past).

## Considered options

1. Keep Nest indefinitely behind a feature flag / dual-run.
2. Archive Nest in a separate repo.
3. **Delete Nest from this monorepo; .NET is the only backend** (chosen).

## Decision outcome

**Chosen option:** 3.

- Delete `apps/backend` (Nest, Fastify adapter, Prisma, Nest tests, Redis streams stub).
- Delete compose Redis nest profile and Nest-only root scripts.
- Remove `@binexus/events` Zod package; event contracts live under `backend/contracts/events`.
- Keep `@binexus/types`, `@binexus/sdk`, `@binexus/ui`, `@binexus/config` for the web/SDK surface.
- No Prisma→EF data migration; local DBs must be recreated. PR rollback is Git.

### Consequences

- Positive: single runtime, clearer CI, no Redis requirement.
- Negative: Nest-era skills/docs need retargeting; historical ADRs remain Nest-flavored until read with supersession status.
- Neutral: ADR-0014 (inventory sync) is unrelated; Nest retirement is this ADR.

## Related decisions

Supersedes Nest-specific parts of:

- ADR-0007 (Nest CQRS bus) → MediatR / handlers in .NET modules
- ADR-0008 (nestjs-pino) → ASP.NET Core structured logging

Amends runtime wording in ADR-0001, ADR-0002, ADR-0005, ADR-0006 without deleting their history.

## Notes

- Gate checkpoints: `docs/migration/gate5-*.md`, `gate6-checkpoint.md`, `gate7-*.md`
- Known insecure local JWT / seed password rejected in Staging/Production (Identity module).
