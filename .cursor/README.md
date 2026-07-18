# `.cursor/` — Project Guidance for Cursor Agents

This directory contains **project-wide guidance** for Cursor's AI agents working on the Binexus platform. It is intentionally versioned so every contributor's agent sessions follow the same conventions.

## What lives here

```
.cursor/
├── rules/   ← always-on or glob-scoped rules (style, security, testing, git, dev workflow)
└── skills/  ← on-demand skills the agent reads when relevant
```

### `rules/`

Markdown files with YAML frontmatter (`description`, `globs`, `alwaysApply`). Cursor injects them into the agent's context based on the frontmatter.

| File                             | Scope                                                            |
| -------------------------------- | ---------------------------------------------------------------- |
| `common-coding-style.md`         | always                                                           |
| `common-development-workflow.md` | always — Plan → TDD → Review → Commit                            |
| `common-git-workflow.md`         | always — Conventional Commits, Husky, branch + PR rules          |
| `common-patterns.md`             | always — repository pattern, API envelope                        |
| `common-security.md`             | always — security checklist                                      |
| `common-testing.md`              | always — Vitest, TDD requirements                                |
| `typescript-coding-style.md`     | `**/*.{ts,tsx,js,jsx}`                                           |
| `typescript-patterns.md`         | `**/*.{ts,tsx,js,jsx}`                                           |
| `typescript-security.md`         | `**/*.{ts,tsx,js,jsx}` — ReDoS, Argon2, Prisma raw query rules   |
| `typescript-testing.md`          | `**/*.{ts,tsx,js,jsx}` — Vitest / Playwright guidance            |
| `graphify.mdc`                   | always — knowledge-graph query hints when `graphify-out/` exists |
| `rust-tauri.mdc`                 | `apps/desktop/**`, `**/src-tauri/**` — Tauri host standards      |
| `rust-coding.mdc`                | `**/*.rs` — idiomatic Rust                                       |
| `rust-security.mdc`              | desktop/capabilities — trust boundary + supply chain             |
| `rust-testing.mdc`               | desktop Rust/TS — unit/integration/E2E expectations              |

### `skills/`

Each skill is a folder with at least `SKILL.md`. The frontmatter `description` is what the agent reads to decide when to invoke the skill.

Workflow:

- `diagnose`, `tdd`, `grill-with-docs`, `improve-codebase-architecture`, `prototype`, `zoom-out`, `documentation-lookup`
- `to-prd`, `to-issues`, `triage`, `handoff`, `write-a-skill`
- `prompt-improver` — researches docs + Notion + code before asking 1-4 targeted questions when a request is ambiguous. Use proactively to avoid wasting a session on the wrong interpretation.
- `learn-codebase` — primes a session in the right order: architecture docs first, active contexts next, code on demand. Token-efficient alternative to "read every file".
- `grill-me` — adversarial pre-mortem on a proposal across 7 axes (security, correctness, multi-tenant, performance, ops, schema, unintended consequences). Use before phase kickoffs and multi-tenant-boundary changes.
- `doc-coauthoring` — long-form docs discipline (per-context pages, ADRs, runbooks, post-mortems, phase specs). Templates + review passes.

Token / context economy:

- `stop-slop` — strips AI tells from prose (PR bodies, commits, docs, Notion). Applies in EN and ES.
- `caveman` — opt-in ultra-terse mode (~75% token cut). Off by default; activate with "caveman" / "modo caveman" / `/caveman`.
- `context-hygiene` — keeps long sessions cheap. Defines Binexus anchors (event names, model names, paths) that must never be summarized away, plus a structured handoff template.
- `graphify` — knowledge-graph code understanding (~71.5× reduction vs naive grep+read). Installed (`graphify` CLI v0.8.22). Always-on rule at `.cursor/rules/graphify.mdc`. Builds `graphify-out/` once via `graphify .`; queries via `graphify query / path / explain`.
- `claude-mem` — opt-in MCP server providing `smart_search` / `smart_outline` / `smart_unfold` (tree-sitter AST navigation) and cross-session persistent memory. 4–18× token savings on code exploration. Install once via `npx claude-mem install --ide cursor`.

Stack-specific:

- `mcp-server-patterns`, `nextjs-turbopack`
- **Rust + Tauri (Branch Client / desktop):**
  - `rust-tauri-handbook` — master index + architecture, libraries, folders, CI/CD, checklists, anti-patterns
  - `rust-tauri` — commands, IPC, plugins, capabilities
  - `rust-architecture` — modular monolith, offline-first, crate boundaries
  - `rust-security` — capabilities, CSP, secrets, audit/deny
  - `rust-fundamentals` — ownership, errors, async, Clippy
  - `rust-tauri-testing`, `rust-tauri-performance`, `rust-sqlite`, `rust-tauri-deployment`
  - `desktop-ux`, `rust-code-review`
- `react-best-practices` — Vercel rules adapted to `apps/web` (Next.js 15 + React 19). Triggers on RSC vs Client, bundle, waterfalls.
- `composition-patterns` — React composition for `apps/web`: avoid boolean-prop explosions, use compound components, lift state via providers.
- `webapp-testing` — Playwright (TypeScript) E2E for `apps/web` against the NestJS backend. Bootstraps `apps/web/e2e/` and gives the day-to-day flow.
- `setup-pre-commit` — wires Husky + lint-staged + commitlint + Prettier + ESLint to mirror CI. Adapted to pnpm + Turborepo + Vitest.
- `react-native` — future driver / ops mobile app via Expo SDK 53+. Bootstrap + monorepo wiring + offline-first proof-of-delivery flow.
- `mcp-builder` — design contract for Binexus's own outward-facing MCP server (`@binexus/mcp-public`). Tool naming, multi-tenant scoping, npm packaging, rate limiting.

Public surface / landing / UX (only for `apps/web/src/app/(public|auth|onboarding)/**`):

- `taste` — anti-slop frontend taste for the public landing, pricing, signup, onboarding. Reads the brief, sets dials, rejects the LLM defaults (AI purple, mesh hero, Inter+slate-900). Does NOT apply to the operator panel.
- `ui-ux-pro` — premium UX patterns + design tokens + conversion patterns for landing → signup → onboarding wizard → first-run. Wraps `ui-ux-pro-max`.
- `react-view-transitions` — native View Transition API for landing section reveals, signup wizard transitions, and shared-element morphs. Not for the operator panel.
- `impeccable` — visual design + audit power-tool. Sub-commands: `shape`, `craft`, `critique`, `audit`, `polish`, `bolder`, `quieter`, `delight`, `optimize`, `live`. Available via `npx impeccable@latest`.
- `web-artifacts` — build standalone single-HTML React + Tailwind + shadcn/ui artifacts (demos, sandboxes, prospect previews) outside `apps/web`. Gitignored output under `artifacts/`.

Growth (landing → signup → activated tenant):

- `customer-research` — JTBD interviews, switch-story diary, competitor profiles. Source of ICP and search vocabulary.
- `growth-seo` — SEO + structured data (schema.org) + programmatic SEO (vs-competitor / city / industry pages) + site IA + AI-overview visibility.
- `marketing-copy` — copy for landing, ads, transactional emails, push, in-app microcopy. Five copy frames. Bilingual ES/EN.
- `cro` — funnel CRO: A/B tests with hypothesis discipline, signup-friction rules, popup policy, paywalls (F7).
- `lifecycle` — trial / drip / onboarding emails with stop-on-success, in-product nudges, referrals, launches, co-marketing, community, churn prevention.
- `messaging` — SMS / WhatsApp / push notifications for customers, drivers, and tenants. Outbox + provider failover.

Phase-specific (F5 Sales → F8 Reporting):

- `pricing` — F5/F7 tier design + value-metric selection + Stripe `Product`/`Price` wiring + LATAM currency rules.
- `sales-outbound` — F5 prospecting + cold email + sales-enablement + SDR↔AE handoff + RevOps CRM mapping.
- `documents` — F7 invoices (incl. CFDI 4.0 for MX) + F4 manifests / PoD packages + F8 XLSX exports. TypeScript stack (`@react-pdf/renderer`, `exceljs`).
- `analytics` — F8 product analytics + tenant-facing dashboards + activation cohorts + KPI catalog. PostHog + Metabase recommendation.
- `aso` — App Store Optimization for the future driver app (title / keywords / screenshots / reviews / privacy nutrition label).

Workflow tools (heavier, opt-in):

- `spec-kit` — GitHub Spec Kit (`specify` CLI, already installed via `uv tool install`). Spec-driven development for slice-larger-than-one-PR work. `/speckit.constitution → /speckit.specify → /speckit.plan → /speckit.tasks → /speckit.implement`.
- `ecc` — Contextual Engineering Coach methodology: working-context tracking, longform/shortform guides, security guide. Use for phase kickoffs and security-sensitive reviews. Templates vendored under `skills/ECC-main/`.
- `understand-anything` — alternative knowledge-graph approach (browser dashboard). Use for onboarding presentations; `graphify` is the default for in-session queries.

Project conventions:

- `semantic-naming` — checks naming for new models, events, commands, shared types, SDK methods, and DTOs before generating code. Backed by [`docs/architecture/naming-conventions.md`](../docs/architecture/naming-conventions.md).
- `notion-docs-sync` — keeps `docs/` and Notion (`Binexus Platform`, `Roadmap`, `Catálogo de eventos`, per-context pages) in lock-step as slices ship.

## What does NOT live here

These are gitignored (see root `.gitignore`):

- `.cursor/hooks/` and `.cursor/hooks.json` — runtime hook scripts that depend on per-machine state
- `.cursor/state/`, `.cursor/cache/`, `.cursor/.local/`, `.cursor/logs/` — runtime, ephemeral, personal
- `.cursor/mcp.json`, `.cursor/mcp.local.json` — MCP server config; can contain tokens or local paths

## Editing rules and skills

- Use the `write-a-skill` skill when authoring a new skill.
- Keep rules concise; agents pay context cost for everything in `alwaysApply: true`.
- Never put paths from your local machine, secrets, or personal tooling preferences here.
- Run `pnpm format` on this directory after editing — Prettier formats Markdown too.
