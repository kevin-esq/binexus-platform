---
name: grill-me
description: Adversarial self-critique on any Binexus proposal, plan, or PR before shipping. Use when the user says "grill me", "challenge this", "pasame los huecos", "qué se rompe", or when a high-stakes decision (phase kickoff, architectural change, pricing, security boundary, multi-tenant invariant) is about to land. Generates a structured cross-examination across security, correctness, performance, multi-tenant, ops, and unintended consequences. Pairs with `diagnose`, `triage`, `improve-codebase-architecture`, and `ecc`'s security guide.
---

# grill-me (Binexus)

Adversarial pre-mortem. Pushes back on a proposal until either it survives or it changes. Adapted from `skills-main/skills/productivity/grill-me` (see [`skills/skills-main/skills/productivity/grill-me/SKILL.md`](../../../skills/skills-main/skills/productivity/grill-me/SKILL.md)).

## When to invoke

- High-stakes architectural decision (ADR-worthy).
- New cross-context flow (e.g. F7 Billing wiring).
- A change to a multi-tenant boundary (`TenantContextService`, `forTenant`, `TENANT_SCOPED_MODELS`).
- Pricing or tier changes that affect existing customers.
- Anything that touches signed URLs / secrets / fiscal documents (CFDI).
- A PR you suspect of being "too easy". The easy ones are where the surprise lives.

NOT for: routine slices that follow the Plan → TDD → Review → Commit cadence. The standard review already grills those.

## The interrogation

When invoked, walk the proposal through the 7 axes below. Treat each as a court cross-examination — pick at the weakest claim until either it strengthens or the proposal changes.

### 1. Security

- What's the worst thing a malicious tenant could do with this?
- What if the user's JWT is replayed after rotation?
- Where does `tenantId` enter the request and who is responsible for verifying it?
- Is there a path that bypasses `TenantContextService.run()`?
- Are signed URLs (MinIO, Stripe checkout) bounded in TTL and scope?
- What logs leak PII? (recipient names, phone numbers, signature images)
- Does any code path read or write outside `TENANT_SCOPED_MODELS`?

### 2. Correctness + invariants

- Which state-machine transitions does this touch? Are they still valid per [`packages/types/src/orders.ts`](../../../packages/types/src/orders.ts) `canTransition()`?
- Idempotency: what happens on retry? Same input, same output, same side effects?
- What's the failure mode when the outbox dispatcher is delayed by 5 minutes? 5 hours?
- What if two operators perform the same action simultaneously?
- Is there a race between the command write and the event emit?
- Are all events emitted within the same transaction as the state change?
- What's the migration path for tenants in the old state?

### 3. Multi-tenant

- Could tenant A ever read tenant B's data through this change?
- Could a system-user action (a cron, an event handler) bleed across tenants?
- Are reads using `forTenant()` consistently?
- Does the read replica respect the same tenant scoping?
- Are there any global counters / sequences that violate tenant isolation?

### 4. Performance

- What's the N in the worst-case query? Is it bounded by tenant size?
- Is there an index for the new query path? Migration in this PR?
- Does the change introduce an N+1 (`include` vs separate queries)?
- What's the impact on the outbox dispatcher's poll loop?
- For the web app: bundle delta? RSC vs Client boundary still respected?
- For the mobile app: payload size on a 3G connection?

### 5. Ops + observability

- Can the on-call engineer debug this from logs alone?
- What metric tells us this is broken in production?
- What runbook entry is missing?
- How do we roll this back? In how many minutes?
- Does the change need a feature flag?
- Is there a "shadow mode" we should ship first?

### 6. Schema + migrations

- Is the migration backward-compatible? Can the old code read the new schema?
- Is the migration forward-compatible? Can the new code read the old schema during deploy?
- Will the migration lock a table > 100ms on production?
- Did we test the migration on a copy of production-sized data?
- Are foreign keys still consistent? Cascade rules correct?

### 7. Unintended consequences

- Who else is touching this code path? Sales? Reporting? Mobile?
- What expectation does this break for an existing customer?
- What expectation does this break for an event consumer in another context?
- What happens if a tenant downgrades after using a feature this PR adds?
- What happens to in-flight resources during the deploy?

## Format

When grilling a proposal, structure the output as:

```markdown
## Proposal grilled

<one-line restatement>

## Surfaces touched

- <context>
- <context>

## Axis 1: Security

- ❓ <question>
- ⚠️ <weakness identified>
- ✅ <claim that survives>

## Axis 2: Correctness + invariants

- ❓ ...

## ... (one per axis)

## Verdict

- Ship as-is: yes / no / with conditions
- Conditions:
  1. <concrete change>
  2. <concrete change>
```

Make verdict explicit. "Looks fine" is not a verdict — list the conditions.

## Worked example (sketch)

User: "Grill me on adding `DELIVERY_FAILED` event with optional `reason` enum."

Output:

```markdown
## Proposal grilled

Add `DELIVERY_FAILED` domain event with `reason: 'no_recipient' | 'wrong_address' | 'damaged' | 'refused' | 'other'`, fired from driver app via SDK after captured photo + GPS.

## Surfaces touched

- logistics
- orders
- messaging
- analytics
- billing (if reason='refused' triggers refund flow)

## Axis 1: Security

- ❓ The driver picks `reason`; can a malicious driver mass-mark deliveries `refused` to dodge work?
  - ⚠️ Yes — we need a server-side anomaly metric (`DELIVERY_FAILED_RATE_PER_DRIVER`) and a tenant-level alert.
- ✅ JWT-scoped per driver session; no cross-tenant risk.

## Axis 2: Correctness + invariants

- ❓ What if `DELIVERY_CONFIRMED` and `DELIVERY_FAILED` both fire for the same stop?
  - ⚠️ Today: not prevented at write-time. Add unique constraint on `(deliveryRouteStopId, terminal=true)`.
- ❓ Order state on `DELIVERY_FAILED`?
  - ⚠️ Not defined — Orders context handler must define a target state (`DELIVERY_FAILED` → `ORDER_RETURN_PENDING`?). Open question, schedule before merging.

## Axis 5: Ops + observability

- ❓ Driver app reports failure offline; what's the SLO for the event reaching the backend?
- ✅ Offline queue exists from F4 Proof Base. Same path.

## Verdict

- Ship as-is: NO.
- Conditions:
  1. Define Orders consumer state transition before the command lands.
  2. Add unique constraint preventing dual terminal states on the same stop.
  3. Add `DELIVERY_FAILED_RATE_PER_DRIVER` metric + tenant alert.
  4. Document expected SLO for offline → online sync.
```

## Tone

Direct, not theatrical. The point is to find weaknesses, not to dunk on the proposer. Avoid:

- "This is obviously broken."
- "Have you even thought about \_\_\_?"
- Phrasing every axis as an attack.

Prefer:

- "What happens when \_\_\_?"
- "I don't see how **_ holds in case _**."
- "The proposal is silent on \_\_\_."

## Anti-patterns

- Grilling a 1-line typo fix. Waste of cycles.
- Grilling without reading the proposal first. You'll produce generic concerns.
- Skipping the verdict. The verdict is the value.
- Refusing to call the proposal "ship as-is yes". When the proposal genuinely survives all 7 axes, say so.

## Pre-PR checklist (when the PR exists because of a grill)

- [ ] The PR description names which conditions from the grill it addresses.
- [ ] Any unmet conditions are explicit follow-ups, with owners.
- [ ] Tests cover at least the conditions raised on axes 1-3 (security, correctness, multi-tenant).

## Reference

- Upstream: [`skills/skills-main/skills/productivity/grill-me/SKILL.md`](../../../skills/skills-main/skills/productivity/grill-me/SKILL.md)
- [`.cursor/skills/diagnose/SKILL.md`](../diagnose/SKILL.md) — debugging counterpart
- [`.cursor/skills/triage/SKILL.md`](../triage/SKILL.md) — issue triage counterpart
- [`.cursor/skills/improve-codebase-architecture/SKILL.md`](../improve-codebase-architecture/SKILL.md)
- [`.cursor/skills/ecc/SKILL.md`](../ecc/SKILL.md) — methodology layer (security guide especially)
- [`.cursor/rules/common-security.md`](../../rules/common-security.md)
- [`.cursor/rules/typescript-security.md`](../../rules/typescript-security.md)
