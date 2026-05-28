---
name: growth-seo
description: Organic growth and search positioning for Binexus's public landing — SEO basics, structured data (schema.org), programmatic SEO, AI-era search visibility, site information architecture, on-page audit. Use when designing the landing's URL structure, when adding metadata to a new `app/(public)` route, when planning a programmatic SEO build (vs-competitor pages, city pages, industry pages), when reviewing Lighthouse SEO, or when auditing why organic traffic stalled. Pairs with `customer-research` (search vocabulary) and `marketing-copy` (page-level copy).
---

# growth-seo (Binexus)

Organic growth + SEO discipline for the Binexus landing. Adapted from `seo-audit`, `ai-seo`, `programmatic-seo`, `schema`, `site-architecture` (see [`skills/marketingskills-main/skills/seo-audit/SKILL.md`](../../../skills/marketingskills-main/skills/seo-audit/SKILL.md), [`ai-seo`](../../../skills/marketingskills-main/skills/ai-seo/SKILL.md), [`programmatic-seo`](../../../skills/marketingskills-main/skills/programmatic-seo/SKILL.md), [`schema`](../../../skills/marketingskills-main/skills/schema/SKILL.md), [`site-architecture`](../../../skills/marketingskills-main/skills/site-architecture/SKILL.md)).

## When to invoke

- Building or restructuring `apps/web/src/app/(public)/**`.
- Adding metadata, `<head>` tags, OG tags, sitemap, robots to a new route.
- Planning programmatic SEO (vs-competitor / city / industry / category pages).
- Reviewing why a route doesn't rank.
- Wiring structured data (`schema.org` JSON-LD).
- Considering "AI Overview" / SGE visibility (the answer-engine era).

## What Binexus is targeting (ICP → search vocabulary)

Pulls from [`customer-research`](../customer-research/SKILL.md). The ICP's actual searches drive the keyword set. Starter buckets:

| Intent          | Vocabulary (ES MX)                                                    | Vocabulary (EN)                                   |
| --------------- | --------------------------------------------------------------------- | ------------------------------------------------- |
| Switch trigger  | "control de entregas", "app para repartidores", "sistema de despacho" | "delivery tracking software", "dispatch software" |
| Comparison      | "Onfleet vs Routific", "Tookan alternativa"                           | "Onfleet alternatives", "Routific competitors"    |
| Local need      | "TMS pequeñas empresas México"                                        | "TMS for SMB", "small fleet management"           |
| Job-to-be-done  | "rutear pedidos automático", "asignar paradas conductores"            | "auto route planning", "driver dispatch"          |
| Compliance (MX) | "CFDI guía de carta porte"                                            | n/a                                               |

Update the keyword sheet quarterly. Stored in `docs/growth/keywords.md`.

## Site information architecture

Three layers max. Visible from the landing in 1-2 clicks.

```
/                                       (Home)
├── /pricing
├── /features
│   ├── /features/orders
│   ├── /features/inventory
│   ├── /features/warehouse
│   ├── /features/logistics
│   └── /features/driver-app           (future, when mobile ships)
├── /solutions                          (industry programmatic)
│   ├── /solutions/restaurants
│   ├── /solutions/ecommerce-fulfillment
│   ├── /solutions/last-mile-distribution
│   └── /solutions/{industry}           (programmatic)
├── /vs                                  (competitor comparison programmatic)
│   ├── /vs/onfleet
│   ├── /vs/routific
│   └── /vs/{competitor}                (programmatic)
├── /cities                              (geo programmatic — MX cities first)
│   ├── /cities/cdmx
│   ├── /cities/monterrey
│   └── /cities/{city}                  (programmatic)
├── /docs                                (public docs)
├── /blog                                (when content motion starts)
├── /about
└── /signup                              (auth surface)
```

Rules:

- One H1 per page. The H1 contains the primary keyword. Never decorative.
- Breadcrumbs on every layer-2 page.
- Internal links: at least 2 contextual links from each page back to a relevant page.
- URL: kebab-case ASCII. No accents in URLs even though copy is ES.
- Canonical: every page has a self-referencing `<link rel="canonical">`. Programmatic pages declare their canonical explicitly.

## Programmatic SEO (the engine)

Generate `/solutions/{industry}`, `/vs/{competitor}`, `/cities/{city}` from a `data/` directory of YAML files + a single template. Each generated page is real, useful, and differentiated. Never thin.

Per-page minimum:

- 500 words of human-relevant copy (not boilerplate that swaps one noun).
- One real customer quote OR a unique data point (e.g. "47% of restaurants in Monterrey using Binexus dispatch ≥ 30 deliveries/day").
- Industry/city-specific imagery (not the same hero photo across all 1,000 pages).
- One unique CTA variant (e.g. `/solutions/restaurants` CTA mentions kitchen-to-curb timing).

If you cannot meet all four, do NOT generate the page. Google penalizes thin programmatic content.

Storage: `data/programmatic/{industry,competitor,city}.yaml` + a generator script. Built at `next build` time, not at request time.

## Structured data (JSON-LD)

Inject in `<head>` per page type. Validate via Google Rich Results Test before merging.

| Page type    | Schema                                                                                           |
| ------------ | ------------------------------------------------------------------------------------------------ |
| Home         | `Organization` + `SoftwareApplication` (Binexus is a SaaS)                                       |
| Pricing      | `Product` for each tier (with `offers` per currency)                                             |
| Features/\*  | `SoftwareApplication` + `FeatureList`                                                            |
| Blog post    | `Article` with `author`, `datePublished`, `image`, `headline`                                    |
| Docs page    | `TechArticle` + `BreadcrumbList`                                                                 |
| `/vs/*`      | `Article` (a comparison is an article; do NOT use `Product` to avoid misrepresenting competitor) |
| FAQ block    | `FAQPage` when there's a real FAQ on the page                                                    |
| Testimonials | `Review` of `SoftwareApplication` (with explicit consent from the customer)                      |

Never fake `aggregateRating` — penalty + reputational damage.

## AI / answer-engine visibility ("AI Overview" / SGE / ChatGPT search)

The search interface is shifting from blue links to model-summarized answers. To stay visible:

1. **Direct answers in the page body.** Each page opens with a 1-2 sentence answer to the implicit question of the page title. Models cite these.
2. **Citations to authority.** Link to manufacturer specs, government regs (SAT for CFDI), Stripe docs for billing claims. Models prefer pages that cite sources.
3. **Definitive numbers.** "Binexus dispatches 99.5 % of routes within 60 seconds" beats "fast". Models love specifics.
4. **HowTo / FAQ structured data.** Already on the schema table above.
5. **`llms.txt`** at the root. Plain text directory of high-signal pages, similar to `robots.txt` but for LLMs.
6. **Don't tilt at JS-only rendering.** Server-render every public page. `next/dynamic` is fine inside components but the page shell is server-side.

## On-page audit checklist (every public route before merge)

- [ ] One `<h1>`. Contains the primary keyword.
- [ ] `<title>` ≤ 60 chars. Includes brand at end: "... | Binexus".
- [ ] Meta description ≤ 155 chars. Human, not a keyword soup.
- [ ] OG image: 1200×630 PNG. Per-route unique.
- [ ] Canonical link.
- [ ] JSON-LD validates.
- [ ] Lighthouse SEO ≥ 95.
- [ ] Lighthouse Performance ≥ 90 (LCP ≤ 2.5s, CLS ≤ 0.1, INP ≤ 200ms).
- [ ] Lighthouse Accessibility ≥ 95 (overlap with [`ui-ux-pro`](../ui-ux-pro/SKILL.md)).
- [ ] Internal: ≥ 2 contextual links to other Binexus pages.
- [ ] External: ≥ 1 citation to a reputable source (when claims are made).
- [ ] No `noindex` accidentally inherited from a parent layout.

## Sitemap + robots

- `apps/web/src/app/sitemap.ts` — Next.js `MetadataRoute.Sitemap`. List all canonical URLs including programmatic.
- `apps/web/public/robots.txt` — allow all, disallow `/api/`, `/admin/`, `/panel/`, `/(auth)/`, `/(onboarding)/`.
- `apps/web/public/llms.txt` — flat list of the high-signal public URLs.

## Anti-patterns

- Tens of thousands of programmatic pages with no per-page differentiation. Pure noise. Don't.
- Stuffing keywords. Modern Google + AI overviews ignore it.
- Cloaking (different content to bots vs humans). Severe penalty.
- Buying links. Severe penalty.
- Hreflang misuse. If you only ship ES-MX, don't claim ES-AR.
- Indexing the onboarding wizard or panel routes.
- Forgetting to update `sitemap.ts` when a new programmatic batch lands.

## Pre-PR checklist

- [ ] Page passes the on-page audit checklist above.
- [ ] Keyword for the page is in `docs/growth/keywords.md`.
- [ ] Page added to `sitemap.ts`.
- [ ] If programmatic: passes the "real / unique / cited / CTA-variant" four-point rule.
- [ ] Copy ran through [`stop-slop`](../stop-slop/SKILL.md).

## Reference

- [`skills/marketingskills-main/skills/seo-audit/SKILL.md`](../../../skills/marketingskills-main/skills/seo-audit/SKILL.md)
- [`skills/marketingskills-main/skills/ai-seo/SKILL.md`](../../../skills/marketingskills-main/skills/ai-seo/SKILL.md)
- [`skills/marketingskills-main/skills/programmatic-seo/SKILL.md`](../../../skills/marketingskills-main/skills/programmatic-seo/SKILL.md)
- [`skills/marketingskills-main/skills/schema/SKILL.md`](../../../skills/marketingskills-main/skills/schema/SKILL.md)
- [`skills/marketingskills-main/skills/site-architecture/SKILL.md`](../../../skills/marketingskills-main/skills/site-architecture/SKILL.md)
- [`.cursor/skills/customer-research/SKILL.md`](../customer-research/SKILL.md) — keyword source of truth
- [`.cursor/skills/marketing-copy/SKILL.md`](../marketing-copy/SKILL.md) — page copy
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md) — visual direction for public pages
