---
description: 'TypeScript/JavaScript security extending common rules'
globs: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx']
alwaysApply: false
---

# TypeScript/JavaScript Security

Extends `common-security.md` with stack-specific patterns for this monorepo (NestJS, Next.js 15, Prisma, Fastify, Argon2, JWT).

## Secret Management

```typescript
// NEVER hardcode
const apiKey = 'sk-proj-xxxxx';

// ALWAYS read from env, validated at module boundary
const apiKey = process.env.OPENAI_API_KEY;
if (!apiKey) {
  throw new Error('OPENAI_API_KEY not configured');
}
```

- All `.env*` files are gitignored. Add new env vars to `.env.example` with safe placeholder values.
- The backend reads env via NestJS `ConfigService`. Don't reach into `process.env` from feature modules; bind it through `ConfigService`.
- Never log secrets. The Pino logger config in `apps/backend/src/common/logger/logger.module.ts` redacts known fields — extend redaction when adding new sensitive headers/fields.

## Input Validation

- Validate at every system boundary (HTTP, queue consumer, CLI, file ingest) with **Zod** (already used in `@binexus/events` schemas) or **class-validator** (used in NestJS DTOs).
- Commands going through `AppCommandBus` are validated via `validateAppCommand` before dispatch — see `apps/backend/src/common/commands/command-validation.ts`. Add validation decorators or `validate()` methods to every new command.

## ReDoS Awareness

- Avoid greedy anchored regex like `/(\s+|\d+)$/` on uncontrolled input. CodeQL alert `js/polynomial-redos` will fire.
- Prefer regex-free linear scans for trivial transforms (see `stripTrailingSlashes` in `packages/sdk/src/client.ts`).
- When a regex is unavoidable, anchor and bound length: reject inputs above a sane cap before matching.

## SQL / Prisma

- Always use Prisma's query API; never string-concatenate SQL.
- For raw queries (rare), use `prisma.$queryRaw\`...\``template literal or`Prisma.sql\`\``— never`$queryRawUnsafe` with user input.
- Multi-tenant queries are scoped automatically via the Prisma extension in `apps/backend/src/common/prisma/prisma.service.ts`. Adding a new tenant-scoped model? Add it to `TENANT_SCOPED_MODELS`.

## Auth & Sessions

- Passwords: **Argon2** (`argon2.hash` / `argon2.verify`) — never bcrypt, scrypt, or plain SHA.
- Tokens: short-lived access JWT + rotating refresh token. Revocation list lives in `RefreshToken` table.
- Roles: enforce via `RolesGuard` + `@Roles()` decorator at controller level. Don't check roles in services.

## HTTP & CSRF

- Next.js routes that mutate state should use Server Actions or POST with origin/SameSite-Strict cookies.
- Don't expose internal IDs in URLs when a UUID/slug would do.

## Pre-Commit Security Check

Before committing, mentally walk:

- [ ] No new hardcoded secrets, keys, or URLs with embedded credentials
- [ ] All new boundary inputs validated (Zod/class-validator)
- [ ] All new regex audited for ReDoS surface
- [ ] All new Prisma queries use parameters, not interpolation
- [ ] All new routes have the right `@Roles()` or `@Public()` decorator
- [ ] All new sensitive log fields added to logger redaction list
