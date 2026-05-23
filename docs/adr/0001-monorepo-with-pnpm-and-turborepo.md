# ADR-0001: Monorepo with pnpm + Turborepo

| Field    | Value                        |
| -------- | ---------------------------- |
| Status   | Accepted                     |
| Date     | 2026-05-23                   |
| Deciders | Kevin Esquivel               |
| Tags     | tooling, monorepo, build, dx |

## Context and problem statement

Binexus is a multi-app, multi-package product: web (Next.js), backend (NestJS), desktop wrapper (Tauri), eventual mobile (Expo), and several shared packages (`types`, `events`, `ui`, `sdk`, `config`). All of them must share TypeScript types, event schemas, and an HTTP SDK — otherwise contracts drift between client and server within hours.

A polyrepo setup would force us to publish internal packages, version-bump them across N repos for every contract change, and chase down stale clients in production. We need a single source of truth and atomic cross-package refactors.

**Question:** how do we structure the workspace?

## Decision drivers

- **Atomic cross-package changes** — a single PR can change an event payload, its schema, the SDK that consumes it, and the UI that renders it.
- **Strict TypeScript everywhere** — shared `tsconfig.base.json` extended by every workspace.
- **One install, one lockfile** — reproducible builds across machines and CI.
- **Fast incremental builds** — sub-second feedback for partial graphs.
- **Single founder, no infra team** — tooling complexity must be near zero.
- **Native deps coexist** (Prisma engines, Argon2, esbuild, sharp) without `pnpm approve-builds` friction.

## Considered options

1. **Polyrepo** — one repo per app/package, published to a private npm registry.
2. **Monorepo with npm/Yarn workspaces** — single repo, native workspaces.
3. **Monorepo with pnpm workspaces + Turborepo** — single repo, content-addressable store, task orchestration.
4. **Monorepo with Nx** — single repo, opinionated task graph + generators.

## Decision outcome

**Chosen option:** _pnpm workspaces + Turborepo_, because it gives us atomic refactors, strict workspace isolation, and a fast cached task graph without the conceptual overhead of Nx generators.

### Positive consequences

- A single `pnpm install` provisions everything; `turbo run build` produces a deterministic dependency-ordered build.
- `workspace:*` protocol keeps internal packages always source-linked — no publish step, no version drift.
- Disk-efficient: pnpm's content-addressable store shares files across workspaces and projects.
- CI caching via Turborepo's task hash gives near-instant re-runs on unrelated changes.

### Negative consequences

- pnpm symlink layout breaks the occasional tool that assumes a flat `node_modules` (we hit this with old ESLint plugins; mitigated by `pnpm.public-hoist-pattern`).
- Native build scripts (Prisma engines, Argon2, esbuild) are blocked by default in pnpm 9+ for security. We had to allowlist them explicitly (see `Validation` below).

### Trade-offs accepted

- New contributors must know pnpm + Turborepo. We mitigate via `CONTRIBUTING.md`.
- We don't get Nx's generators or its consistency-first project graph; we trade that flexibility for less magic.

## Pros and cons of the options

### Option 1 — Polyrepo

- **Good:** Each app has perfect autonomy; teams can ship independently.
- **Good:** Repos are smaller; cloning is fast.
- **Bad:** Cross-package contract changes are 3+ PRs across 3+ repos.
- **Bad:** Requires a private npm registry (Verdaccio / GitHub Packages); more infra.
- **Bad:** Internal version drift is the default state; contracts decay silently.
- **Bad:** Worst possible fit for a single-founder, contract-heavy product.

### Option 2 — npm/Yarn workspaces

- **Good:** Built into the package manager; zero new dependencies.
- **Bad:** No task orchestration — every script is a custom shell pipeline.
- **Bad:** Slower installs, no content-addressable store.
- **Bad:** Workspace boundaries are weakly enforced (any package can import any other by relative path).

### Option 3 — pnpm + Turborepo _(chosen)_

- **Good:** Strict workspace isolation enforced by pnpm symlinks.
- **Good:** Turborepo task graph + caching is the simplest fast-feedback story.
- **Good:** Massive disk/install savings via the content-addressable store.
- **Bad:** Native build scripts require explicit allowlist (`onlyBuiltDependencies`).
- **Bad:** Some tools mis-resolve under symlinks (rare in 2026; we ignore the long tail).

### Option 4 — Nx

- **Good:** Powerful project graph, generators, plugins, computation caching.
- **Good:** Strong opinions reduce decision fatigue.
- **Bad:** Steep learning curve, opinionated to the point of lock-in.
- **Bad:** Heavier conceptual model than we need at single-founder scale.
- **Bad:** Generators encourage breadth before depth — exactly what _"Foundation wide. Execution narrow."_ warns against.

## Validation

This decision is working if:

- A typical contract change (event schema → SDK → consumer) lands in **one PR**.
- `turbo run build` from a cold cache stays under 90 s on commodity laptops.
- We never need to `pnpm publish` an internal package.

It is failing if:

- We start chaining `--filter` flags manually because Turborepo's graph is wrong.
- pnpm's symlink layout repeatedly breaks new tools.
- Cross-package refactors require coordinating multiple commits.

## More information

- [pnpm workspaces](https://pnpm.io/workspaces)
- [Turborepo handbook](https://turborepo.com/docs)
- [`pnpm.onlyBuiltDependencies` rationale](https://pnpm.io/settings#onlybuiltdependencies)
- Related: ADR-0002 (modular monolith), ADR-0007 (command bus)
