---
name: stop-slop
description: Strip AI tells from prose before delivering. Use when writing commit messages, PR bodies, Binexus docs (`docs/`), Notion content (`Binexus Platform`, `Roadmap`, `Catálogo de eventos`, per-context pages), changelog entries, or any user-facing text. Apply both in English and Spanish (the workspace mixes both). Skip for code, JSON, and SQL.
---

# stop-slop (Binexus)

Eliminate predictable AI writing patterns before shipping any human-readable text in this repo. Applies to PRs, commits, `docs/`, Notion pages, and skill/rule edits.

Adapted from Hardik Pandya's `stop-slop` skill (MIT). The full reference lives in [`skills/stop-slop-main/SKILL.md`](../../../skills/stop-slop-main/SKILL.md) and [`skills/stop-slop-main/references/`](../../../skills/stop-slop-main/references/).

## Core rules

1. **Cut filler.** No "just", "really", "basically", "actually", "simply", "sure!", "happy to help", "in summary". Drop all adverbs unless they carry weight.
2. **Break formulaic structures.** No "not X, it's Y". No three-item rhetorical lists. No dramatic fragmentation. No "here's what / this / that" throat-clearing.
3. **Active voice.** Every sentence has a human (or named system) subject doing something. No "the decision emerges", no "the complaint becomes a fix".
4. **Be specific.** "Updated the config file" is wrong — name `config/redis.ts`. "The implications are significant" is wrong — name the implication. No lazy extremes ("every", "always", "never") doing vague work.
5. **Vary rhythm.** Mix sentence lengths. Two items beat three. No em dashes.
6. **Trust the reader.** State facts directly. Skip softening, justification, hand-holding.

## Binexus-specific

- **Commits / PR titles** follow Conventional Commits: `feat(<context>): <imperative subject>`. The subject is one phrase, no filler. See [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md).
- **PR bodies** follow [`.github/pull_request_template.md`](../../../.github/pull_request_template.md). Each section answers one question. "What" is a bullet list of facts. "Why" is one paragraph.
- **`docs/domains/*.md`, `docs/states/*.md`, `docs/events/README.md`** are reference docs. Lead with the noun being defined. No "Let's explore...". No "The system uses...". Just: "`DeliveryRoute` is the aggregate root for...".
- **Notion `Ahora` callout** on `Binexus Platform`: one slice in progress, one PR queued, one next step. No marketing copy.
- **Spanish prose** (Notion, some commit subjects in transcripts) follows the same rules. Drop "básicamente", "de hecho", "simplemente", "en realidad". No "no es X, es Y".

## Quick checks before delivering

- Any adverb? Justify it or kill it.
- Any passive voice? Find the actor, make them the subject.
- Inanimate doing a human verb ("the migration decides")? Rename the actor.
- Throat-clearing opener ("Here's what changed:", "In this PR:")? Cut to the bullet.
- Three consecutive sentences of similar length? Break one.
- Em dash anywhere outside a code block? Replace with comma or period.
- Vague declarative ("this improves performance")? Quantify it or describe the mechanism.
- Generic Notion update ("everything looks great")? Replace with the specific state change.

## What not to touch

- Code comments — they're already covered by [`.cursor/rules/common-coding-style.md`](../../rules/common-coding-style.md): comments only when they explain non-obvious intent.
- SQL inside migrations.
- JSON, YAML, or `.env.example` content.
- ADR text inside `docs/adr/` once an ADR is accepted — those are append-only.

## Reference

- [`.github/pull_request_template.md`](../../../.github/pull_request_template.md)
- [`.cursor/rules/common-git-workflow.md`](../../rules/common-git-workflow.md)
- [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md)
- Original skill: [`skills/stop-slop-main/SKILL.md`](../../../skills/stop-slop-main/SKILL.md)
