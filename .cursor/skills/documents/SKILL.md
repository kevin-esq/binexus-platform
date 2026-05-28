---
name: documents
description: Generate production-grade business documents from Binexus data — invoices (F7 Billing), shipping labels, route manifests, monthly statements (F7), operational reports (F8 Reporting), CSV/XLSX exports of orders or deliveries, and PDF documents for legal/regulatory needs (CFDI, signed PODs). Use when adding any "Download/Export PDF/XLSX/DOCX" feature, when wiring CFDI/e-invoice generation, when exporting reporting data, or when generating a shareable artifact from tenant data. Adapted from Anthropic's `pdf`, `xlsx`, `docx`, `pptx` skills.
---

# documents (Binexus)

Business document generation for F7 Billing (invoices, statements, CFDI), F4 Logistics (route manifests, signed PODs), and F8 Reporting (XLSX exports, monthly summaries). Combines the upstream PDF, XLSX, DOCX, PPTX skills into one Binexus-aware entry point.

Upstream skills (Anthropic, Complete-terms-in-LICENSE.txt):

- [`skills/skills-mainb/skills/pdf/SKILL.md`](../../../skills/skills-mainb/skills/pdf/SKILL.md)
- [`skills/skills-mainb/skills/xlsx/SKILL.md`](../../../skills/skills-mainb/skills/xlsx/SKILL.md)
- [`skills/skills-mainb/skills/docx/SKILL.md`](../../../skills/skills-mainb/skills/docx/SKILL.md)
- [`skills/skills-mainb/skills/pptx/SKILL.md`](../../../skills/skills-mainb/skills/pptx/SKILL.md)

## When to invoke

| Trigger                                              | Format                   | Phase       |
| ---------------------------------------------------- | ------------------------ | ----------- |
| "Generate invoice for order/period"                  | PDF (+ CFDI XML when MX) | F7          |
| "Export orders / deliveries to spreadsheet"          | XLSX                     | F8 + ad-hoc |
| "Monthly statement for tenant"                       | PDF                      | F7          |
| "Route manifest for driver to print"                 | PDF                      | F4 / now    |
| "Signed PoD package for legal claim"                 | PDF                      | F4 + legal  |
| "Tenant-facing report (KPIs, deliveries, on-time %)" | PDF + XLSX               | F8          |
| "Investor deck / partner proposal" (one-off)         | PPTX                     | rare        |

DOCX is rare for Binexus. Reserve for contract templates (NDA, MSA) when partner work begins. PPTX is only for non-product artifacts.

## TypeScript stack (Binexus-native)

Binexus is TypeScript-first. The upstream skills are written for Python — they describe the underlying file format, which is language-agnostic, but the **implementation should be TS** to stay in the monorepo. Use these libraries:

| Format    | Library (TS)                          | Why                                                                                               |
| --------- | ------------------------------------- | ------------------------------------------------------------------------------------------------- |
| PDF       | `@react-pdf/renderer`                 | JSX-style declarative PDFs. Lives well inside the Binexus codebase, server-side render in NestJS. |
| PDF (alt) | `pdfkit`                              | When you need imperative drawing (signatures, dynamic positioning).                               |
| XLSX      | `exceljs`                             | Active maintenance. Streams large workbooks. Supports formulas, formatting, images.               |
| CFDI XML  | `@binexus/cfdi` (to be created in F7) | Mexican e-invoice. Wraps `xml-js` + signature flow per the SAT spec.                              |
| DOCX      | `docx`                                | Native TS, builds DOCX without a Word dep. Use only when truly needed.                            |
| PPTX      | (skip)                                | Generate via a third party (Canva, Slides) when needed. Keep PPTX out of the build pipeline.      |

Install only the ones you need. Each format used should justify its bundle weight.

## Where document generation lives

```
apps/backend/src/contexts/billing/documents/      # F7 invoices, CFDI, statements
apps/backend/src/contexts/logistics/documents/    # route manifests, PoD packages
apps/backend/src/contexts/reporting/documents/    # F8 exports
packages/documents/                                # shared templates + utilities (cross-context)
```

Templates live in TypeScript. Never generate documents on the client.

## Multi-tenant rules

Every document carries a tenant. The generator MUST:

1. Take a `tenantId` argument.
2. Resolve tenant branding from `TenantBranding` (logo, business name, fiscal data) via `forTenant()`.
3. Resolve locale from tenant settings (`es-MX`, `es-CO`, `es-AR`, `en-US`).
4. Resolve currency from tenant settings.
5. Reject if any of the above is missing.

A document MUST NOT silently fall back to a Binexus default — fiscal documents misrepresenting a tenant are a legal problem.

## PDF invoice template (F7 anchor)

The reference template for F7. All other PDF documents inherit shape:

```
Header
├── Tenant logo (left)                 ├── Invoice number (right)
├── Tenant business name + fiscal ID    ├── Issue date
├── Tenant address                      ├── Due date
Recipient
├── Customer business name + fiscal ID  ├── Customer address
Lines
├── Description | Qty | Unit price | Total per line (in tenant currency)
Totals
├── Subtotal | Tax (IVA / VAT) | Total
Footer
├── Payment instructions                 ├── Page n of m
├── QR code (for CFDI when MX)           ├── Stamp + signature
```

Rules:

- Type the data into the template via a Zod-validated DTO; never raw query results.
- Page break: lines fit ≥10 per page; force a new page if remaining < 4 lines.
- Currency formatting: `Intl.NumberFormat(locale, { style: 'currency', currency })` — never string concat.
- Dates: `Intl.DateTimeFormat` with the tenant locale. Never `toLocaleString()` without explicit locale.
- File name: `<tenantSlug>-invoice-<sequentialId>.pdf` — never include the customer's PII in the file name.

## XLSX export rules

For F8 Reporting and ad-hoc exports:

- One workbook per export. Multiple sheets per logical grouping (orders / deliveries / failed deliveries).
- First row: column headers, frozen, bold.
- Date columns formatted as `yyyy-mm-dd hh:mm` with a timezone column next to them.
- Currency columns formatted with the tenant currency code.
- Big exports stream rows; never hold the whole result set in memory.
- File size limit: 50 MB. Above that, split by month or page.
- Never include `tenantId` cleartext in user-visible cells; it's not PII but it's also not useful to the recipient.

## Storage + delivery

- Generated documents are stored in MinIO under `tenant-documents/<tenantId>/<yyyy>/<mm>/<filename>`.
- Pre-signed URL with 15-minute TTL for the user to download.
- The URL never goes in a log line.
- The MinIO bucket has versioning enabled. Documents are immutable once issued.
- Audit log: every generation writes an `AuditEvent` (`DOCUMENT_GENERATED`) with `tenantId`, `userId`, `documentType`, `referenceId` (orderId, periodId, …).

## CFDI (Mexico) — F7 specific

CFDI 4.0 is the SAT specification. Binexus is responsible for:

1. Issuing the XML.
2. Signing with the tenant's `.cer + .key + password` (stored encrypted, NEVER in env).
3. Stamping via a PAC (Solución Factible, Konesh, etc. — TBD per tenant).
4. Storing both XML and the PAC-returned PDF.
5. Emitting `INVOICE_ISSUED` event in F7 once stamped.
6. Storing the PAC response with the timbre fiscal digital.

`@binexus/cfdi` will encapsulate this. Until then, do not roll a CFDI generator inside a feature PR — schedule the F7 spike first.

## Localization

- ES-MX, ES-CO, ES-AR, EN-US at minimum.
- Number, date, currency formatting via `Intl`.
- Tax labels per country: IVA (MX/AR/CO), VAT (EN-default).
- Fiscal ID labels: RFC (MX), NIT (CO), CUIT (AR), TIN (US).
- Page sizes: Letter for MX/US, A4 for CO/AR.

## Anti-patterns

- Building a document on the client. Always server-side.
- Concatenating currency: `${amount} ${currency}`. Use `Intl.NumberFormat`.
- Hardcoding the tenant ID in test PDFs and accidentally shipping. Use a `__TEST__` watermark on every non-prod doc.
- Reusing the same `invoiceNumber` after a regenerate. Numbers are sequential per tenant and immutable.
- Storing the signing key in `process.env`. Use a per-tenant encrypted KMS reference.
- Generating a CFDI without a PAC stamp. The XML alone has no fiscal validity.

## Pre-PR checklist

- [ ] Tenant locale + currency + branding resolved before generation.
- [ ] Zod-validated DTO into the template.
- [ ] File name has no PII.
- [ ] Audit log entry written.
- [ ] MinIO upload uses tenant-scoped prefix.
- [ ] Pre-signed URL TTL ≤ 15 min.
- [ ] No PII in any log line.
- [ ] If CFDI: PAC stamping is in the path, not a TODO.

## Reference

- [`skills/skills-mainb/skills/pdf/SKILL.md`](../../../skills/skills-mainb/skills/pdf/SKILL.md)
- [`skills/skills-mainb/skills/xlsx/SKILL.md`](../../../skills/skills-mainb/skills/xlsx/SKILL.md)
- [`skills/skills-mainb/skills/docx/SKILL.md`](../../../skills/skills-mainb/skills/docx/SKILL.md)
- [`skills/skills-mainb/skills/pptx/SKILL.md`](../../../skills/skills-mainb/skills/pptx/SKILL.md)
- [`.cursor/skills/pricing/SKILL.md`](../pricing/SKILL.md) — invoice line model
- [`.cursor/skills/analytics/SKILL.md`](../analytics/SKILL.md) — feeds F8 exports
- Stripe plugin: `mcps/plugin-stripe-stripe`
