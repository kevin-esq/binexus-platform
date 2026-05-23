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

- [ ] `apps/backend`
- [ ] `apps/web`
- [ ] `apps/desktop`
- [ ] `packages/`
- [ ] `infrastructure/`
- [ ] `docs/`

## Bounded context(s)

- [ ] identity
- [ ] orders
- [ ] inventory
- [ ] sales
- [ ] logistics
- [ ] cross-cutting / foundation

## Checklist

- [ ] Conventional Commit title (`feat|fix|chore|docs|refactor|test|build|ci|perf|style|revert`)
- [ ] `pnpm exec turbo run typecheck lint build` is green locally
- [ ] New or changed events have a Zod schema in `packages/events`
- [ ] State machine changes are reflected in `docs/states/<entity>.md` AND in `packages/types`
- [ ] Multi-tenant: every new tenant-scoped Prisma model is added to `TENANT_SCOPED_MODELS`
- [ ] No `any` introduced (or justified inline with a comment)
- [ ] Docs updated (`docs/architecture/*`, `docs/domains/*`, `docs/events/README.md`, `README.md`)
- [ ] No secrets or credentials committed

## Screenshots / videos (UI changes only)

<!-- Drop them here -->

## Out of scope / follow-ups

<!-- Anything intentionally left for another PR -->
