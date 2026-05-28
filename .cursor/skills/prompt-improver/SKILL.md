---
name: prompt-improver
description: Turn a vague Binexus request into a researched, scoped, ready-to-execute plan before writing code. Use when the request is missing scope, bounded context, target slice, or success criteria — e.g. "add billing", "mejora logistics", "haz el siguiente bloque", "refactor". Use proactively to avoid wasting a session on the wrong interpretation. Pairs with Plan Mode and the `notion-docs-sync` skill.
---

# prompt-improver (Binexus)

Stop ambiguous prompts before they cost a session. Research the codebase + Notion, then ask 1-4 grounded questions, then execute.

Adapted from `claude-code-prompt-improver-main/skills/prompt-improver`; full reference: [`skills/claude-code-prompt-improver-main/skills/prompt-improver/SKILL.md`](../../../skills/claude-code-prompt-improver-main/skills/prompt-improver/SKILL.md).

## When to invoke

Invoke before doing any work whenever the request is missing one of:

- **Bounded context** ("billing" vs "orders" vs "logistics"). See [`docs/architecture/bounded-contexts.md`](../../../docs/architecture/bounded-contexts.md).
- **Slice / phase** (F1 Orders, F2 Inventory, F3 Warehouse, F4 Logistics, F5 Sales, F6 Catalog+Customers, F7 Billing, F8 Reporting). See Notion `Roadmap` (id `36b91920-017f-81e5-bf8a-c83e484c1bc6`).
- **Concrete outcome** (a new command? a new event? a UI screen? a docs-only PR?).
- **Acceptance signal** (tests? typecheck green? deploy?).

Heuristic: if you cannot fill the [`.github/pull_request_template.md`](../../../.github/pull_request_template.md) "What / Why / How" sections from the prompt alone, invoke this skill.

## Workflow

### 1. Research first (cheap before expensive)

Run in this order. Stop as soon as you have enough to ask targeted questions.

1. **Conversation history.** Re-read the user's recent turns. The slice may already be defined.
2. **`docs/`** for the affected context — `docs/domains/<context>.md`, `docs/states/<aggregate>.md`, `docs/events/README.md`, `docs/workflows/<workflow>.md`.
3. **Notion** — `Binexus Platform` Home `Ahora` callout, `Roadmap`, per-context page. Use `notion-search` + `notion-fetch`. The page IDs are in [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md).
4. **Codebase** — quick `Grep` for the noun being requested (model, event, command). Use a `Task subagent_type=explore` if more than 2-3 files are involved.

Anchor every question in something you actually found. Speculation makes the question useless.

### 2. Ask 1-4 questions via `AskQuestion`

Use the `AskQuestion` tool, not prose bullets. Each question:

- Targets one decision point.
- Offers 2-4 concrete options sourced from research.
- Includes the trade-off in the option label when it matters.

Question budget:

- 1-2 questions: pick which slice / which context (e.g. "Logistics proof base vs failed delivery vs liquidation?").
- 3-4 questions: scope + approach + acceptance.
- Never more than 4. If you need more, you don't have enough research yet.

### 3. Confirm the slice plan in Plan Mode

After the answers, switch to Plan Mode (`SwitchMode target_mode_id=plan`) and submit a plan via `CreatePlan` that fills:

- **Recommendation** — which slice and why.
- **Scope** — bullet list aligned to the PR template.
- **Key files** — concrete paths with `[label](path)` links.
- **Behavior** — one mermaid diagram for cross-context flow.
- **Validation** — which tests + which `turbo run` targets.
- **Out of scope** — explicit follow-ups so the next slice is not pre-loaded.

### 4. Execute

Only after the plan is accepted, run the slice. Honour [`.cursor/rules/common-development-workflow.md`](../../rules/common-development-workflow.md) (Plan → TDD → Review → Commit) and the `notion-docs-sync` skill.

## Question templates for Binexus

**Choosing a slice inside an active phase:**

> The next F4 slice is either (a) proof base (DeliveryProof model + optional confirm payload), (b) failed delivery (`DELIVERY_FAILED` event + stop status), or (c) route liquidation (cash reconciliation). Which one ships next?

**Choosing scope inside a slice:**

> For Delivery Proof Base, do you want this slice to include the MinIO presigned upload endpoint, or stay at object-key references and ship the upload flow in a follow-up?

**Cross-context boundaries:**

> `ORDER_DELIVERED` is currently emitted but unconsumed. Should Billing's `RegisterReceivableCommand` consume it now (F7 starts), or stay in placeholder until F7 begins formally?

**Acceptance criteria:**

> Acceptance for this slice: (a) typecheck/lint/build green, (b) unit tests on the new handler, (c) integration test against Prisma. Which level is required to land?

## Anti-patterns

- Asking the user to choose between options you have not researched ("Should we use Redis or PostgreSQL?" without checking `infrastructure/compose/docker-compose.yml`).
- Asking >4 questions at once.
- Skipping Notion (`Ahora` callout) — that callout often is the answer.
- Jumping into code on an ambiguous prompt and apologising later. The cost of one round of `AskQuestion` is far less than one wasted slice.

## Reference

- [`.cursor/skills/notion-docs-sync/SKILL.md`](../notion-docs-sync/SKILL.md)
- [`.cursor/skills/to-prd/SKILL.md`](../to-prd/SKILL.md) — when the request is product-level rather than slice-level
- [`.cursor/skills/zoom-out/SKILL.md`](../zoom-out/SKILL.md) — when the user wants the whole picture before any slice
- Original skill: [`skills/claude-code-prompt-improver-main/skills/prompt-improver/SKILL.md`](../../../skills/claude-code-prompt-improver-main/skills/prompt-improver/SKILL.md)
