---
name: web-artifacts
description: Build standalone, bundled HTML artifacts (React + Tailwind + shadcn/ui) for demos, internal tools, design previews, sandboxed experiments, or content the user wants to drop into a chat/Slack/Notion as a self-contained file. Use when the user asks for a one-off interactive page, a clickable demo of an operator flow, a tenant-facing PDF-alternative, or a public preview that does NOT need to live in `apps/web/`. Do NOT use for anything that belongs in the production web app.
---

# web-artifacts (Binexus)

Build self-contained HTML artifacts that live OUTSIDE the production app. Adapted from Anthropic's `web-artifacts-builder`: [`skills/skills-mainb/skills/web-artifacts-builder/SKILL.md`](../../../skills/skills-mainb/skills/web-artifacts-builder/SKILL.md).

The upstream skill builds artifacts for claude.ai (single bundled `index.html`). For Binexus the same stack works for any one-off web artifact we want to share without deploying.

## When to use

- Click-through demo of a not-yet-built operator flow (e.g. driver app mockup) shared with a customer.
- Internal mini-tool (e.g. CSV → seed JSON converter for our `apps/backend/prisma/seed.ts`).
- Landing variant we want to A/B test in a sandbox before merging into `apps/web`.
- Public preview of a feature we sent to a prospect: "open this HTML file in a browser, no setup".
- Cursor Canvas alternative when the deliverable must work without Cursor.

## When NOT to use

- Production landing → use `apps/web/src/app/(public)/**` directly.
- Production operator UI → use `apps/web/src/app/**`.
- Multi-page real product → that's the web app.
- Data visualizations the user will iterate on inside Cursor → use Cursor Canvas (see the `canvas` skill).

## Where artifacts live

```
artifacts/
├── <slug>/                  # one project per artifact
│   ├── package.json
│   ├── index.html
│   ├── src/
│   │   ├── App.tsx
│   │   └── ...
│   ├── tailwind.config.ts
│   ├── vite.config.ts
│   └── README.md            # purpose + audience + delivery URL
└── README.md                # index of artifacts
```

`artifacts/` is gitignored by default (do not commit the bundled `bundle.html`). If we decide to keep an artifact under version control (e.g. a landing prototype), check it in but keep `node_modules/` and `dist/` ignored.

## Stack (matches the upstream skill)

- React 18 + TypeScript via Vite.
- Tailwind CSS 3.4.1 + shadcn/ui theming.
- Parcel (bundling to single HTML file via `html-inline`).
- Node 18+ (we're on 22 — compatible).

## Workflow

### 1. Initialize

```bash
mkdir -p artifacts && cd artifacts
bash <(curl -fsSL https://raw.githubusercontent.com/anthropics/skills/main/skills/web-artifacts-builder/scripts/init-artifact.sh) <slug>
cd <slug>
```

Or, if you cloned the upstream skill repo (it's already at `skills/skills-mainb/skills/web-artifacts-builder/`):

```bash
bash skills/skills-mainb/skills/web-artifacts-builder/scripts/init-artifact.sh artifacts/<slug>
cd artifacts/<slug>
```

This creates a fully-configured Vite + React + TS + Tailwind + shadcn/ui + Parcel project.

### 2. Build the artifact

Edit `src/App.tsx`. The artifact is a single-file React tree — no router unless you really need one.

Defaults to follow:

- Use the design tokens from [`apps/web/src/lib/design-tokens.ts`](../../../apps/web/src/lib/design-tokens.ts) when the artifact represents a Binexus surface. Copy the relevant token values into the artifact so it stays self-contained, but keep them visually consistent.
- Apply [`taste`](../taste/SKILL.md) bans (no AI purple, no centered hero over dark mesh, no Inter+slate-900 default, no em dashes in copy).
- One shadcn theme per artifact (`zinc`, `slate`, `stone`). Do NOT mix shadcn themes.

### 3. Bundle to a single HTML file

```bash
bash skills/skills-mainb/skills/web-artifacts-builder/scripts/bundle-artifact.sh
```

Produces `bundle.html` — a self-contained file with JS, CSS, and assets inlined. Ready to share via email / Slack / Notion / drop into a Cursor Canvas.

### 4. Deliver

- Internal demo → drop `bundle.html` into Notion or Slack.
- Customer share → upload to a static host (MinIO bucket dedicated to demos, or Vercel / Netlify drop). Set short TTL.
- Cursor preview → open `bundle.html` in the user's browser via `start bundle.html` (PowerShell).

## Style discipline (the same anti-slop rules as the production app)

VERY important: artifacts often slide into AI slop because they're "throwaway". They are not throwaway when a prospect or investor opens them. Apply [`taste`](../taste/SKILL.md):

- Avoid excessive centered layouts.
- No purple gradients.
- No uniform rounded corners on every element.
- No Inter as the default font (use Geist for Binexus-branded artifacts).
- No em dashes.
- One accent color, locked.
- One icon family (Phosphor).

## When to graduate

If an artifact gets used more than twice, or someone asks "can I link to this from the landing?", it has graduated. Steps:

1. Migrate the artifact into `apps/web/src/app/(public)/<route>/page.tsx`.
2. Reuse `packages/ui` primitives instead of shadcn inlines.
3. Replace inlined tokens with imports from `apps/web/src/lib/design-tokens.ts`.
4. Delete the `artifacts/<slug>` folder.

## Pre-ship checklist (for any artifact about to be shown to someone)

- [ ] Lighthouse a11y ≥ 90 on the bundled file.
- [ ] `bundle.html` opens in Safari, Chrome, and Firefox.
- [ ] No Binexus secrets, tenant data, or production URLs hard-coded.
- [ ] One accent color, one icon family.
- [ ] Copy ran through [`stop-slop`](../stop-slop/SKILL.md).
- [ ] `README.md` in the artifact folder names: purpose, audience, delivery URL, TTL.

## Anti-patterns

- Hard-coding production URLs (`https://api.binexus.com/...`) — use placeholders.
- Inlining real tenant data — use realistic but fake data.
- Reaching for chart libraries (recharts, nivo, visx) when CSS + a few SVGs would do.
- Building artifacts that pretend to do real work (e.g. "this button creates a real tenant"). Always show fake state or no-op.

## Reference

- Upstream skill: [`skills/skills-mainb/skills/web-artifacts-builder/SKILL.md`](../../../skills/skills-mainb/skills/web-artifacts-builder/SKILL.md)
- Upstream scripts: [`skills/skills-mainb/skills/web-artifacts-builder/scripts/`](../../../skills/skills-mainb/skills/web-artifacts-builder/scripts/)
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md)
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md)
- Cursor Canvas (skill `canvas` in your global skills) — preferred when the deliverable is meant to live inside Cursor.
