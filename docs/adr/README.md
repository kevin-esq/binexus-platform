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

| #    | Title                                                                                                                   | Status                  | Date       |
| ---- | ----------------------------------------------------------------------------------------------------------------------- | ----------------------- | ---------- |
| 0001 | [Monorepo with pnpm + Turborepo](0001-monorepo-with-pnpm-and-turborepo.md)                                              | Accepted (amended 0015) | 2026-05-23 |
| 0002 | [Modular monolith over microservices](0002-modular-monolith-architecture.md)                                            | Accepted (amended 0015) | 2026-05-23 |
| 0003 | [Offline-first by design](0003-offline-first-design.md)                                                                 | Accepted                | 2026-05-23 |
| 0004 | [Event-driven with Outbox pattern](0004-event-driven-with-outbox-pattern.md)                                            | Accepted                | 2026-05-23 |
| 0005 | [Multi-tenant: shared database + `tenantId`](0005-multi-tenant-shared-database.md)                                      | Accepted (amended 0015) | 2026-05-23 |
| 0006 | [Authentication: JWT + Argon2 + refresh rotation + RBAC](0006-authentication-jwt-argon2-rbac.md)                        | Accepted (amended 0015) | 2026-05-23 |
| 0007 | [Command bus: CQRS-lite on `@nestjs/cqrs`](0007-command-bus-cqrs-lite.md)                                               | Superseded by ADR-0015  | 2026-05-23 |
| 0008 | [Structured logging with Pino](0008-structured-logging-with-pino.md)                                                    | Superseded by ADR-0015  | 2026-05-23 |
| 0009 | [Feature flags: tenant-scoped, DB-backed](0009-feature-flags-tenant-scoped.md)                                          | Accepted                | 2026-05-23 |
| 0010 | [GitHub workflow: Modern Rulesets with CI fallback](0010-github-modern-rulesets-with-ci-fallback.md)                    | Accepted                | 2026-05-23 |
| 0011 | [Failed delivery — order pause state and route completion](0011-failed-delivery-order-and-route-completion.md)          | Accepted                | 2026-07-04 |
| 0012 | [Route liquidation — COD cash reconciliation](0012-route-liquidation-cod-reconciliation.md)                             | Accepted                | 2026-07-04 |
| 0013 | [Sales / POS — sub-slices and session-first retail model](0013-sales-pos-sub-slices-and-session-model.md)               | Accepted                | 2026-07-10 |
| 0014 | [Sync inventory reservation and authenticated tenant context](0014-inventory-sync-reservation-and-tenant-middleware.md) | Accepted                | 2026-07-11 |
| 0015 | [NestJS retirement — .NET 10 as sole backend](0015-nestjs-retirement-dotnet-sole-backend.md)                            | Accepted                | 2026-07-12 |
| 0016 | [Runtime modes for Cloud and Branch](0016-runtime-modes-cloud-vs-branch.md)                                             | Proposed                | 2026-07-12 |
| 0017 | [Branch runtime responsibilities](0017-branch-runtime.md)                                                               | Proposed                | 2026-07-12 |
| 0018 | [Branch Server per sucursal](0018-branch-server.md)                                                                     | Proposed                | 2026-07-12 |
| 0019 | [Device identity for Branch and Tauri hosts](0019-device-identity.md)                                                   | Proposed                | 2026-07-12 |
| 0020 | [Terminal identity as a logical POS role](0020-terminal-identity.md)                                                    | Proposed                | 2026-07-12 |
| 0021 | [LAN discovery for Branch Server](0021-lan-discovery.md)                                                                | Proposed                | 2026-07-12 |
| 0022 | [Branch device pairing and handshake](0022-pairing-and-handshake.md)                                                    | Proposed                | 2026-07-12 |
| 0023 | [Branch installation topology](0023-branch-installation.md)                                                             | Proposed                | 2026-07-12 |
| 0024 | [Branch local HTTP API](0024-local-http-api.md)                                                                         | Proposed                | 2026-07-12 |
| 0025 | [Branch local authentication](0025-local-authentication.md)                                                             | Proposed                | 2026-07-12 |
| 0026 | [Offline-first strategy for Branch Runtime](0026-offline-first-strategy.md)                                             | Proposed                | 2026-07-12 |
| 0027 | [Branch and Cloud synchronization architecture](0027-synchronization-architecture.md)                                   | Proposed                | 2026-07-12 |
| 0028 | [Branch Runtime conflict resolution](0028-conflict-resolution.md)                                                       | Proposed                | 2026-07-12 |
| 0029 | [Branch Runtime bootstrap snapshot](0029-bootstrap.md)                                                                  | Proposed                | 2026-07-12 |
| 0030 | [Branch Runtime configuration storage](0030-configuration-storage.md)                                                   | Proposed                | 2026-07-12 |
| 0031 | [Branch Runtime secrets storage](0031-secrets-storage.md)                                                               | Proposed                | 2026-07-12 |
| 0032 | [Branch Runtime Windows Service deployment](0032-windows-service-deployment.md)                                         | Proposed                | 2026-07-12 |

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
