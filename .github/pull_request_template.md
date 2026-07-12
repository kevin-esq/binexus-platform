<!-- Title format: <type>(<scope>): <subject>  e.g. feat(orders): add CreateOrderCommand -->

## What

<!-- 1-3 bullet points: what does this PR change? -->

-

## Why

<!-- Business / technical reason. Link the issue or doc if there is one. -->

Closes #

## How

<!-- Brief mention of the architectural decision, new commands/events, state transitions, etc. -->

## Affected areas

- [ ] `apps/backend/` (.NET)
- [ ] `apps/web`
- [ ] `apps/desktop`
- [ ] `packages/`
- [ ] `infrastructure/`
- [ ] `docs/`

## Bounded context(s)

- [ ] identity
- [ ] orders
- [ ] inventory
- [ ] warehouse
- [ ] sales
- [ ] logistics
- [ ] cross-cutting / foundation

## Checklist

- [ ] Conventional Commit title (`feat|fix|chore|docs|refactor|test|build|ci|perf|style|revert`)
- [ ] `pnpm exec turbo run typecheck lint build` is green locally
- [ ] `dotnet test apps/backend/Binexus.slnx -c Release` is green when backend changes
- [ ] New or changed domain events documented in `docs/events/README.md` (+ optional `docs/events/schemas/`); runtime names/payloads live in `apps/backend/src/Modules/`
- [ ] State machine changes reflected in `docs/states/<entity>.md` AND in `packages/types` when UI-facing
- [ ] Multi-tenant: new tenant-scoped EF entities use global query filters / tenant middleware
- [ ] No `any` introduced (or justified inline with a comment)
- [ ] Docs updated (`docs/architecture/*`, `docs/domains/*`, `docs/events/README.md`, `README.md`)
- [ ] No secrets or credentials committed

## Screenshots / videos (UI changes only)

<!-- Drop them here -->

## Out of scope / follow-ups

<!-- Anything intentionally left for another PR -->
