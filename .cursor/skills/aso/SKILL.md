---
name: aso
description: App Store Optimization for the future Binexus driver / ops mobile app(s) on the Apple App Store and Google Play. Use when preparing the store listing (title, subtitle, description, keywords, screenshots, video preview), when reviewing why install conversion is low, when localizing the listing per LATAM country (MX/CO/AR), or when planning a launch on the stores. Pairs with `react-native` (the app itself), `marketing-copy` (text), `taste` (visuals).
---

# aso (Binexus)

App Store Optimization for `apps/mobile/` (and any future tenant-admin companion). Adapted from [`skills/marketingskills-main/skills/aso/SKILL.md`](../../../skills/marketingskills-main/skills/aso/SKILL.md).

This skill activates when [`react-native`](../react-native/SKILL.md) is in flight — i.e. when the driver app is preparing for store submission. Until then, it's reference material.

## When to invoke

- Preparing the App Store / Google Play listing for the driver app.
- Localizing the listing for MX / CO / AR / US (EN).
- Reviewing why install conversion is low after launch.
- Picking the screenshot / preview-video set per locale.
- Updating the listing after a major feature ships (e.g. offline mode, signature capture).

## What ranks on each store (high level)

| Field                  | Apple App Store                                          | Google Play                  |
| ---------------------- | -------------------------------------------------------- | ---------------------------- |
| Title (highest weight) | 30 chars                                                 | 50 chars                     |
| Subtitle / Short desc  | 30 chars (subtitle)                                      | 80 chars (short description) |
| Long description       | Not indexed for keywords — read by humans                | Indexed for keywords         |
| Keywords field         | 100 chars, comma-separated (indexed, NOT shown to user)  | No separate field            |
| Categories             | Primary + secondary                                      | Primary + tags               |
| Reviews + ratings      | Major ranking signal                                     | Major ranking signal         |
| Install velocity       | Major ranking signal                                     | Major ranking signal         |
| Localization           | One listing per locale; counts as separate ranks per geo | Same                         |

## Binexus listing template

### Title (max 30 / 50 chars)

> "Binexus — Conductor"

Stores the brand front-loaded; the role suffix means "this is the driver app" and helps users on a tenant's pre-shared link distinguish it from the panel.

When `apps/mobile-admin` ships later: "Binexus — Despacho" for the admin companion.

### Subtitle / Short description

ES (30 / 80 chars):

> "Rutas, paradas, pruebas de entrega — todo desde tu celular"

EN:

> "Driver app for routes, stops, and proof of delivery"

### Long description (Apple) / Description (Google) — same body, different field

5-7 short paragraphs:

1. **Opening line** — one sentence outcome. Customer's vocabulary (from [`customer-research`](../customer-research/SKILL.md)).
2. **Capabilities** — bulleted list (Apple allows bullets in newer iOS), 4-6 items.
3. **Offline + reliability** — drivers care about working in airplane mode. Call it out.
4. **Branding / tenant-aware** — explain that the app is tenant-scoped (drivers log in with credentials given by their dispatcher).
5. **Privacy** — what data the app collects and what it doesn't.
6. **Support** — `hola@binexus.com` + the docs URL.
7. **Footer** — language list, accessibility note, "made in" if relevant.

Forbidden: "AI-powered" without justification, "best app for X" (Apple flags), competitor names (both stores penalize), emojis in the body (allowed but tacky for a B2B driver app).

### Keywords field (Apple only — 100 chars total)

Pack with the search vocabulary from [`growth-seo`](../growth-seo/SKILL.md):

```
ruta,entrega,pedido,conductor,repartidor,despacho,logistica,gps,firma,foto,offline,flota,TMS
```

Rules:

- Singular form; Apple matches plural automatically.
- No spaces around commas.
- No keywords that are already in the title / subtitle (waste).
- No brand names you don't own (legal).

### Screenshots

| Slot | Apple iPhone 6.7" (1290×2796)   | Google Play (1080×1920+) |
| ---- | ------------------------------- | ------------------------ |
| 1    | "Ve tus rutas del día"          | Same                     |
| 2    | "Confirma con foto + firma"     | Same                     |
| 3    | "Funciona sin internet"         | Same                     |
| 4    | "Mapa con paradas en secuencia" | Same                     |
| 5    | "GPS, ETA y notas por parada"   | Same                     |
| 6    | Optional — testimonials         | Optional                 |

Design rules:

- Big device + cropped UI. Overlay text on the device, not next to it.
- Single accent color per locale, matching the brand. Honour [`taste`](../taste/SKILL.md).
- Overlay text in the locale's language; do NOT screenshot the EN UI for an ES listing.
- First 2 screenshots are the most-seen — they decide install or not. Lead with the highest-value capability.

### Preview video (Apple) / Promo video (Google)

30 seconds. No voice-over necessary. Step-through of the daily flow:

1. Driver opens app, sees today's route.
2. Taps the first stop. Navigates with the in-app map.
3. Arrives, taps "Confirmar".
4. Captures photo + signature.
5. Sees the stop turn green; moves to next.

Caption every step (ES + EN versions). Quiet background music if any.

## Localization

Minimum locales for launch:

- ES-MX (primary)
- ES-419 (catch-all for other LATAM Spanish, with light tweaks for CO / AR copy if needed)
- EN-US (for the US market, optional in V1)

Per locale:

- Title / subtitle / description / screenshots re-rendered. NOT auto-translated.
- Customer support email is the same (`hola@binexus.com`); response language matches the locale.

## Ratings + reviews

- In-app rating prompt: trigger after **3 confirmed deliveries** (the moment of "this is working"). Never before.
- Use Apple / Google's native rating API (`expo-store-review`); do NOT roll a custom prompt.
- Respond publicly to every 1-3 star review within 48 h. Short, factual, no apologies.
- A negative review followed by an unaddressed bug fix is a permanent install-rate drag.

## Install velocity (the dark-art ranking signal)

Both stores reward bursts:

- Coordinate launch with [`lifecycle`](../lifecycle/SKILL.md) launch programs (Product Hunt, partner promo).
- Push the link to tenants via in-product banner + email simultaneously.
- Avoid drip — concentrated installs > distributed installs for the first 2 weeks.

## Privacy nutrition label (Apple)

Apple requires the privacy "label" filled out accurately. For the Binexus driver app:

| Data type             | Collected? | Linked to user?       | Used for tracking?                     |
| --------------------- | ---------- | --------------------- | -------------------------------------- |
| Identifiers (user ID) | Yes        | Yes (driver username) | No                                     |
| Location (precise)    | Yes        | Yes                   | No (only with consent at confirm time) |
| Photos                | Yes        | Yes                   | No                                     |
| Diagnostics           | Yes        | No                    | No                                     |

Lying on the label is an App Store rejection + reputational risk.

## Anti-patterns

- Keyword-stuffing the title ("Binexus | Route Manager Pro Driver TMS Logistics") — Apple rejects.
- Using the same generic icon used elsewhere. The icon is the first install-decision signal.
- Skipping localization. ES with an EN screenshot is a discount-bin signal.
- Promising features in the listing that aren't shipped yet. Removal + ban risk.
- Buying installs / reviews. Permanent ban risk.
- Ignoring 1-star reviews. They compound.

## Pre-PR / pre-submit checklist

- [ ] Listing translates to ES-MX (primary) + at least one secondary locale.
- [ ] Screenshots match the current build's UI (regenerate per release).
- [ ] Keywords match the [`growth-seo`](../growth-seo/SKILL.md) vocabulary; no overlap with title/subtitle.
- [ ] Privacy label completed accurately.
- [ ] In-app rating prompt wired to 3-confirms threshold.
- [ ] Deep links tested.
- [ ] Support email monitored.
- [ ] Launch coordinated with [`lifecycle`](../lifecycle/SKILL.md).

## Reference

- [`skills/marketingskills-main/skills/aso/SKILL.md`](../../../skills/marketingskills-main/skills/aso/SKILL.md)
- [`.cursor/skills/react-native/SKILL.md`](../react-native/SKILL.md)
- [`.cursor/skills/marketing-copy/SKILL.md`](../marketing-copy/SKILL.md)
- [`.cursor/skills/taste/SKILL.md`](../taste/SKILL.md)
- [`.cursor/skills/growth-seo/SKILL.md`](../growth-seo/SKILL.md)
- [`.cursor/skills/lifecycle/SKILL.md`](../lifecycle/SKILL.md)
