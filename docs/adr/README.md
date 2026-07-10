# Architecture Decision Records (ADRs)

This folder holds the **immutable** record of every non-trivial architectural decision made on the Binexus platform.

> ADRs are append-only. A decision that no longer applies is **superseded**, not deleted.

## Format

We use [MADR](https://adr.github.io/madr/) (Markdown Architecture Decision Records) — full form. See [`template.md`](template.md).

## Lifecycle

```
Proposed  →  Accepted  →  (Deprecated | Superseded by ADR-NNNN)
```

- **Proposed** — under discussion. PR open.
- **Accepted** — current truth. Code must honor it.
- **Deprecated** — no longer apply. Kept for history.
- **Superseded by ADR-NNNN** — replaced by a newer decision; link the successor.

## How to add a new ADR

1. Copy [`template.md`](template.md) to `NNNN-kebab-case-title.md` using the next free number.
2. Fill **all** sections. If a section doesn't apply, write _"Not applicable"_ explicitly.
3. Set `Status: Proposed`. Open a PR.
4. On merge, flip `Status: Accepted` and append the ADR to the index below.
5. If this ADR replaces another, set the predecessor's status to `Superseded by ADR-NNNN`.

## Index

| #    | Title                                                                                                          | Status   | Date       |
| ---- | -------------------------------------------------------------------------------------------------------------- | -------- | ---------- |
| 0001 | [Monorepo with pnpm + Turborepo](0001-monorepo-with-pnpm-and-turborepo.md)                                     | Accepted | 2026-05-23 |
| 0002 | [Modular monolith over microservices](0002-modular-monolith-architecture.md)                                   | Accepted | 2026-05-23 |
| 0003 | [Offline-first by design](0003-offline-first-design.md)                                                        | Accepted | 2026-05-23 |
| 0004 | [Event-driven with Outbox pattern](0004-event-driven-with-outbox-pattern.md)                                   | Accepted | 2026-05-23 |
| 0005 | [Multi-tenant: shared database + `tenantId`](0005-multi-tenant-shared-database.md)                             | Accepted | 2026-05-23 |
| 0006 | [Authentication: JWT + Argon2 + refresh rotation + RBAC](0006-authentication-jwt-argon2-rbac.md)               | Accepted | 2026-05-23 |
| 0007 | [Command bus: CQRS-lite on `@nestjs/cqrs`](0007-command-bus-cqrs-lite.md)                                      | Accepted | 2026-05-23 |
| 0008 | [Structured logging with Pino](0008-structured-logging-with-pino.md)                                           | Accepted | 2026-05-23 |
| 0009 | [Feature flags: tenant-scoped, DB-backed](0009-feature-flags-tenant-scoped.md)                                 | Accepted | 2026-05-23 |
| 0010 | [GitHub workflow: Modern Rulesets with CI fallback](0010-github-modern-rulesets-with-ci-fallback.md)           | Accepted | 2026-05-23 |
| 0011 | [Failed delivery — order pause state and route completion](0011-failed-delivery-order-and-route-completion.md) | Accepted | 2026-07-04 |
| 0012 | [Route liquidation — COD cash reconciliation](0012-route-liquidation-cod-reconciliation.md)                    | Accepted | 2026-07-04 |
| 0013 | [Sales / POS — sub-slices and session-first retail model](0013-sales-pos-sub-slices-and-session-model.md)      | Accepted | 2026-07-10 |

## When NOT to write an ADR

- "We renamed `userId` to `accountId`." → commit message.
- "We bumped Next.js to 15.4." → changelog / Dependabot PR.
- "We added a new endpoint." → API docs.

## When to ALWAYS write an ADR

- Choosing or replacing a database, queue, transport, or runtime.
- Changing how cross-context communication works.
- Changing the tenancy, auth, or authorization model.
- Adopting (or dropping) a paradigm: CQRS, ES, micro-frontends, etc.
- Anything that future-you will ask: _"why on earth did we do this?"_
