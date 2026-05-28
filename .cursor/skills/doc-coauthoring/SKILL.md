---
name: doc-coauthoring
description: Collaborative authoring of long-form Binexus documents — public docs (apps/web/src/app/(public)/docs/**), per-context domain pages (docs/domains/*.md), runbooks, post-mortems, ADRs, and the eventual customer-facing knowledge base. Use when writing or rewriting docs that span multiple contributors, when extracting tribal knowledge into a doc, when prepping a doc for external review (auditor, customer, partner), or when planning a docs-only PR. Pairs with `notion-docs-sync`, `stop-slop`, and `marketing-copy`.
---

# doc-coauthoring (Binexus)

Discipline for long-form Binexus docs that more than one person touches. Adapted from Anthropic's `doc-coauthoring` (see [`skills/skills-mainb/skills/doc-coauthoring/SKILL.md`](../../../skills/skills-mainb/skills/doc-coauthoring/SKILL.md)).

Not for: code comments (use [`.cursor/rules/common-coding-style.md`](../../rules/common-coding-style.md)), commit messages (use [`stop-slop`](../stop-slop/SKILL.md) + [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md)), or marketing copy (use [`marketing-copy`](../marketing-copy/SKILL.md)).

## What counts as a "co-authored doc" in Binexus

| Doc                                    | Lives in                                                                  | Owner                |
| -------------------------------------- | ------------------------------------------------------------------------- | -------------------- |
| Architecture overview                  | [`docs/architecture/overview.md`](../../../docs/architecture/overview.md) | All engineers        |
| Per-context domain pages               | [`docs/domains/<context>.md`](../../../docs/domains)                      | Context owner        |
| Event catalog                          | [`docs/events/README.md`](../../../docs/events/README.md)                 | Engineer ships event |
| State-machine pages                    | [`docs/states/<aggregate>.md`](../../../docs/states)                      | Context owner        |
| Workflow walkthroughs                  | [`docs/workflows/*.md`](../../../docs/workflows)                          | Engineer ships flow  |
| ADRs                                   | `docs/adr/<n>-<title>.md`                                                 | Author + reviewers   |
| Public customer docs (when they exist) | `apps/web/src/app/(public)/docs/**`                                       | DocsLead + eng       |
| Runbooks                               | `docs/runbooks/*.md`                                                      | On-call engineer     |
| Post-mortems                           | `docs/postmortems/<date>-<slug>.md`                                       | Incident lead        |
| Phase specs                            | `docs/phases/<n>-<name>.md`                                               | PhaseLead            |

## The seven rules

### 1. One doc, one job

Each doc answers ONE question. Multiple jobs become multiple docs with cross-links.

- `docs/domains/logistics.md` answers "what does the logistics context own?".
- A separate `docs/workflows/route-dispatch.md` answers "how does dispatch happen end to end?".
- Conflating them loses both readers.

### 2. Lead with the noun being defined

The first sentence names the subject in subject-first form. Not "Let's talk about routes." Not "Routes are introduced here."

> "`DeliveryRoute` is the aggregate root that owns a sequence of `DeliveryRouteStop`s assigned to a driver."

### 3. Show the shape before the prose

Before paragraphs, show the structure: a code block, a mermaid diagram, a table, a state machine. Then prose explains it.

### 4. Mermaid for behavior, tables for taxonomy

- Mermaid when the answer is a sequence or a state machine.
- Tables when the answer is a categorical list (event catalog, command catalog, status enum, tier comparison).
- Code blocks for actual code or syntax.
- Prose only for the connective tissue.

### 5. Anchor every cross-reference

When a doc references another concept, link to the file/heading by path. Never "see the orders doc" — always [`docs/domains/orders.md`](../../../docs/domains/orders.md). Future moves break the prose; pinned links break loudly enough to fix.

### 6. Date significant claims; flag unverified

When a doc says "X happens", anchor it. If the source is a metric, link to the dashboard or the PR. If the claim is provisional ("expected to land in F7"), tag it `(F7 target — not yet implemented)`.

### 7. Update in the same PR that changed the behavior

A behavior change without a doc change is incomplete. Block the PR. This is encoded in [`.cursor/rules/common-development-workflow.md`](../../rules/common-development-workflow.md) Plan → TDD → Review → Commit.

## Authoring workflow for a new doc

1. **Outline first.** 5-15 headings. Get review on the outline before writing prose.
2. **One section at a time.** Don't write the whole doc, then ask for review. Write the outline + section 1; review; section 2; review.
3. **Cite, don't paraphrase.** When stating a fact that lives in code, link the file/line. When stating a fact that lives in Notion, link the Notion page.
4. **Pair with [`stop-slop`](../stop-slop/SKILL.md)** on the final pass.
5. **Notion sync** — if the doc has a Notion counterpart, use [`notion-docs-sync`](../notion-docs-sync/SKILL.md) in the same PR.

## Reviewing someone else's doc

Three passes:

- **Pass 1 — outline only.** Skim headings. Is the doc answering one question? Are the sections in a useful order?
- **Pass 2 — line-level.** Every sentence: does it earn its place? `stop-slop` + active voice.
- **Pass 3 — references.** Click every link. Stale links are the highest-cost defect in docs.

Single comments. No "lgtm" review on a doc PR.

## Templates

### Per-context domain doc

```markdown
# `<context>` context

## Purpose

<one sentence>

## Aggregates

- `<Aggregate>` — <one-line ownership>

## Commands

| Command | Purpose | Idempotent? | Emits |
| ------- | ------- | ----------- | ----- |

## Events

| Event name | Carries | Consumed by |
| ---------- | ------- | ----------- |

## States

See [`docs/states/<aggregate>.md`](../states/<aggregate>.md).

## HTTP surface

- POST /<contextSlug>/...
- GET /<contextSlug>/...

## UI

- `apps/web/src/app/<route>/page.tsx`

## Workflows

- [`docs/workflows/<workflow>.md`](../workflows/<workflow>.md)

## Out of scope

- <explicit non-goals>
```

### ADR

```markdown
# <n>. <title>

Status: Proposed | Accepted | Superseded by <n+x>
Date: YYYY-MM-DD
Authors: <names>

## Context

<3-6 sentences>

## Decision

<the decision in 2-3 sentences>

## Consequences

- Positive: <bullets>
- Negative: <bullets>

## Alternatives considered

- <option> — rejected because <reason>

## Implementation pointers

- <file path / commit / phase>
```

Accepted ADRs are append-only. If the decision changes, write a new ADR that supersedes it.

### Post-mortem

```markdown
# YYYY-MM-DD — <slug>

## TL;DR

<one sentence>

## Timeline (UTC)

- HH:MM — <event>
- HH:MM — <event>

## Impact

- Tenants affected: <N>
- Data affected: <description>
- Duration: <X>

## Root cause

<2-3 paragraphs>

## What went well

- <bullets>

## What went badly

- <bullets>

## Action items

| #   | Owner | Action | Due |
| --- | ----- | ------ | --- |
```

Post-mortems are blameless. Reviewer enforces this.

## Anti-patterns

- "We" used to mean "Binexus the company" interchangeably with "we the authors of this doc". Pick one referent per doc.
- "TBD" left for weeks. TBD is a deadline; tag it with a date.
- A doc that contradicts the code. Code wins; the doc PR must update.
- Long prose paragraphs that hide a table. If you list three things in prose, table them.
- Embedding screenshots of code. Use a code block.
- Diagrams as PNGs without source. Use mermaid (text-versioned) or commit the source file alongside the PNG.
- A 3000-word doc that didn't go through an outline review.

## Pre-PR checklist

- [ ] Outline reviewed (or doc is short enough that outline = the doc).
- [ ] One job per doc.
- [ ] Subject-first opening sentence.
- [ ] Mermaid for behavior, tables for taxonomy, code blocks for code.
- [ ] All cross-references are full paths.
- [ ] [`stop-slop`](../stop-slop/SKILL.md) pass done.
- [ ] [`notion-docs-sync`](../notion-docs-sync/SKILL.md) updated if a Notion counterpart exists.
- [ ] If the doc references a behavior, the PR includes either that behavior or a link to the PR that introduced it.

## Reference

- Upstream: [`skills/skills-mainb/skills/doc-coauthoring/SKILL.md`](../../../skills/skills-mainb/skills/doc-coauthoring/SKILL.md)
- [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md)
- [`.cursor/skills/stop-slop/SKILL.md`](../stop-slop/SKILL.md)
- [`.cursor/skills/semantic-naming/SKILL.md`](../semantic-naming/SKILL.md)
- [`.cursor/rules/common-development-workflow.md`](../../rules/common-development-workflow.md)
