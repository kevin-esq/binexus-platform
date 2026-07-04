# ADR-0012: Route liquidation — COD cash reconciliation on completed routes

| Field    | Value                                                         |
| -------- | ------------------------------------------------------------- |
| Status   | Accepted                                                      |
| Date     | 2026-07-04                                                    |
| Deciders | Kevin Esquivel                                                |
| Tags     | logistics, orders, f4, cash, cod, state-machine, events, rbac |

## Context and problem statement

Slice **#4 (Route Liquidation)** closes the operational cash loop for F4 Logistics. After slice #3/#3b, a delivery route can reach `COMPLETED` with a mix of `DELIVERED`, `FAILED`, and (future) `SKIPPED` stops. Drivers and supervisors must reconcile **cash collected on delivery (COD)** against what the system expected before the route is considered financially closed at the branch.

Today:

- `DeliveryProof` captures delivery evidence only (recipient, photo/signature, GPS) — **no payment amount**.
- `Order` has `totalCents` but **no `paymentMethod`**; all orders look alike in the panel.
- `DeliveryRouteLiquidation`, `DELIVERY_ROUTE_LIQUIDATED`, and `SettleOrderCommand` are documented but **not implemented**.
- Billing (F7) is planned; boundary docs already assign **operational cash capture to Logistics** and **payment allocation / receivables to Billing**.

Real tenants mix **COD (`CASH`)**, **prepaid card/transfer**, and **B2B credit**. Route liquidation in v1 must reconcile **only `CASH` orders**; other methods are out of scope for the arqueo amount.

**Questions answered:** (A) where the liquidation model lives, (B) granularity, (C) failed stops, (D) whether/when orders move to `SETTLED`, (E) payment method on orders, (F) workflow shape, (G) discrepancy policy and authorization.

## Decision drivers

- **Operational truth before accounting** — Logistics records what cash the driver/supervisor declares; Billing remains future financial truth (ADR boundary in `docs/domains/logistics.md` / `billing.md`).
- **Mixed payment reality** — not every delivered order expects cash on the route.
- **ADR-0011 compatibility** — `FAILED` stops do not block route completion and must not inflate COD expected totals.
- **Panel clarity without Billing** — operators need an unambiguous “done vs. pending” signal; `SETTLED` semantics must not contradict a future Billing module.
- **Single-session UX (F2)** — arqueo is a ~2-minute panel workflow, not a multi-day draft.
- **Supervisor accountability on exceptions (G3)** — discrepancies require elevated role + reason; happy path stays fast (B3 hybrid).
- **Existing RBAC** — reuse `Role` enum (ADR-0006); avoid new roles unless none fit.

## Considered options

### Decision A — Where the liquidation model lives

1. **A1 — Logistics pure** — `DeliveryRouteLiquidation` aggregate in Logistics; emits `DELIVERY_ROUTE_LIQUIDATED`.
2. **A2 — Split Logistics + Orders in one transaction** — Logistics persists liquidation and Orders settles in the same command (no event).
3. **A3 — Billing-owned** — defer to F7.

### Decision B — Granularity

1. **B1 — Route total only** — one declared amount vs. one expected total.
2. **B2 — Stop-level always** — line per delivered COD stop.
3. **B3 — Hybrid** — route total by default; **stop breakdown required only when declared ≠ expected** (chosen).

### Decision C — Failed / skipped stops

1. **C1 — Exclude from expected** — only `DELIVERED` stops with `paymentMethod = CASH` count (chosen; ADR-0011).
2. **C2 — Block liquidation while any stop is `FAILED`**.
3. **C3 — List failed stops at $0 in every liquidation**.

### Decision D — When does the order move to `SETTLED`? (**pending final approval**)

1. **D1 — Ops-complete `SETTLED`, split by payment method (recommended)** — see [Decision D — pending](#decision-d--pending-final-approval) below.
2. **D2 — `SETTLED` only for COD on liquidation; others stay `DELIVERED`** — panel uses badges for prepaid/credit.
3. **D3 — Defer all `SETTLED` to Billing (F7)** — liquidation is route-only; orders stay `DELIVERED`.

### Decision E — Expected amount / payment method

1. **E1 — Assume all orders are COD (`order.totalCents`)**.
2. **E2 — `paymentMethod` on `Order`** — reuse `PAYMENT_REGISTERED` methods: `CASH | CARD | TRANSFER | CREDIT`; only `CASH` enters route liquidation expected totals (chosen).
3. **E3 — Snapshot expected cents on stop at confirm delivery**.

### Decision F — Workflow

1. **F1 — Start + Close** two commands / draft state.
2. **F2 — Single command** — atomic liquidate in one session (chosen).

### Decision G — Discrepancies and authorization

1. **G1 — Record discrepancy, any authenticated user closes**.
2. **G2 — Block close if declared ≠ expected**.
3. **G3 — Allow close with discrepancy only for supervisor roles + mandatory reason** (chosen).

**Supervisor roles (no new role required):** `ADMIN` and `SUPER_ADMIN` per ADR-0006. `RolesGuard` + `@Roles()` exist but are not yet applied to Logistics endpoints — slice #4 wires the first production use. `DRIVER`, `WAREHOUSE`, and `CASHIER` are not authorized to close with discrepancy in v1.

## Decision outcome

| Decision | Choice | Notes                                                                                                                            |
| -------- | ------ | -------------------------------------------------------------------------------------------------------------------------------- |
| **A**    | **A1** | Logistics owns aggregate + event.                                                                                                |
| **B**    | **B3** | Route total; breakdown on mismatch only.                                                                                         |
| **C**    | **C1** | `FAILED` / `SKIPPED` / non-`DELIVERED` stops excluded.                                                                           |
| **D**    | **D1** | Ops-complete `SETTLED`: `CASH` on route liquidation; `CARD`/`TRANSFER` on delivery; `CREDIT` stays `DELIVERED` until Billing F7. |
| **E**    | **E2** | Prisma + types + create-order API.                                                                                               |
| **F**    | **F2** | `LiquidateDeliveryRouteCommand` (single shot).                                                                                   |
| **G**    | **G3** | `ADMIN` \| `SUPER_ADMIN` + `discrepancyReason` when `declaredCents ≠ expectedCents`.                                             |

### Decision D — Order `SETTLED` (chosen: D1)

**Chosen: D1 — Ops-complete `SETTLED`, split by payment method.**

| `paymentMethod`    | When order becomes `SETTLED`                                                                |
| ------------------ | ------------------------------------------------------------------------------------------- |
| `CASH`             | On `DELIVERY_ROUTE_LIQUIDATED` when the order was included in the route's COD expected set. |
| `CARD`, `TRANSFER` | On `DELIVERED` (extend `MarkOrderDeliveredCommand` / `DELIVERY_CONFIRMED` handler).         |
| `CREDIT`           | **Stays `DELIVERED`** until Billing (F7).                                                   |

Document **`SETTLED` = operational closure** in [`docs/states/order.md`](../states/order.md); fiscal settlement remains Billing F7.

### Positive consequences

- COD reconciliation is explicit, auditable, and route-scoped.
- Mixed payment tenants are modeled honestly via `paymentMethod`.
- Hybrid UX keeps the common case fast; exceptions are supervisor-gated.
- `DELIVERY_ROUTE_LIQUIDATED` gives Billing a clean hook for F7 without implementing Billing now.
- Reuses existing payment method enum values; no parallel taxonomy.

### Negative consequences

- Logistics must read `Order.totalCents` and `paymentMethod` for delivered stops (first cross-context read in liquidation path — read-only, tenant-scoped).
- `SETTLED` semantics require careful docs if D1 is chosen (ops vs. fiscal).
- `paymentMethod` on existing rows needs a migration default (`CASH`) — historical orders behave as COD until corrected.
- First production `@Roles()` usage — must be tested.

### Trade-offs accepted

- **A3 / F1 discarded** — delays business value; docs already planned two-phase commands but product chose single session.
- **B2 always-on discarded** — too slow for daily arqueo; hybrid covers audit when it matters.
- **E1 discarded** — wrong for mixed-payment tenants.
- **G2 discarded** — blocks real ops when cash doesn't match exactly.

## Pros and cons of the options

### A1 — Logistics pure (chosen)

- **Good:** Matches bounded-context docs; event hook for Billing.
- **Bad:** Cross-context read of Order fields for expected totals.

### D1 — Ops-complete SETTLED, split triggers (recommended, pending)

- **Good:** No limbo for prepaid; COD closes on arqueo; CREDIT path clear for F7.
- **Bad:** `SETTLED` meaning must be documented; two triggers to maintain.

### D2 — SETTLED only for COD

- **Good:** Minimal `MarkOrderDelivered` change.
- **Bad:** Prepaid orders sit in `DELIVERED` looking unfinished.

### D3 — Defer SETTLED to F7

- **Good:** No semantic risk with Billing.
- **Bad:** Panel confusion; `#4` feels incomplete to operators.

## Validation

- Supervisor can liquidate a `COMPLETED` route with only `DELIVERED` + `CASH` stops; `expectedCents` = sum of those order totals; `declaredCents` match → closes without line breakdown.
- Route with `DELIVERED` + `FAILED` mixes: failed orders excluded from expected; liquidation succeeds on delivered COD only.
- `declaredCents ≠ expectedCents` without breakdown → rejected; with breakdown + non-supervisor role → `403`; with `ADMIN` + reason → accepted.
- Second liquidation on same route → idempotent error / conflict.
- Liquidation on non-`COMPLETED` route → rejected.
- Route with zero COD expected (`declaredCents = 0`) → allowed (all prepaid/credit/failed).
- If **D1** approved: `CARD`/`TRANSFER` orders → `SETTLED` on delivery; `CASH` → `SETTLED` only after liquidation event; `CREDIT` stays `DELIVERED`.
- Re-evaluation if: tenants need partial liquidations, driver self-declaration mobile flow, or Billing requires renaming `SETTLED`.

## More information

- Related ADRs: [ADR-0011](0011-failed-delivery-order-and-route-completion.md), [ADR-0006](0006-authentication-jwt-argon2-rbac.md), [ADR-0004](0004-event-driven-with-outbox-pattern.md)
- Related docs: [`docs/domains/logistics.md`](../domains/logistics.md), [`docs/domains/orders.md`](../domains/orders.md), [`docs/domains/billing.md`](../domains/billing.md), [`docs/states/order.md`](../states/order.md)
- Planned event schema: `packages/events/src/schemas/delivery-route-liquidated.ts` (to be added in slice #4)
