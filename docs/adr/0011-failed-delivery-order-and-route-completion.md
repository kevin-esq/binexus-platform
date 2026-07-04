# ADR-0011: Failed delivery — order pause state and route completion with terminal stops

| Field    | Value                                        |
| -------- | -------------------------------------------- |
| Status   | Accepted                                     |
| Date     | 2026-07-04                                   |
| Deciders | Kevin Esquivel                               |
| Tags     | logistics, orders, f4, state-machine, events |

## Context and problem statement

Slice **#3 (`ReportFailedDeliveryCommand`)** closes the operational loop for F4 Logistics: a driver or dispatcher must be able to report that a stop could not be delivered, Orders must reflect that fact, and the delivery route must be able to close even when some stops failed.

Two product questions were open:

1. **What happens to the order when delivery fails?** Today the happy path is `OUT_FOR_DELIVERY → DELIVERED` via `DELIVERY_CONFIRMED`. There is no failure state; `DeliveryRouteStopStatus.FAILED` exists in Prisma but is unused.
2. **Can a route close with a mix of delivered and failed stops?** Today `ConfirmDeliveryCommand` completes a route only when every stop is `DELIVERED`; a `FAILED` stop would block `COMPLETED` indefinitely.

Resolution of failed orders (re-queue for retry or cancel) is explicitly **out of scope** for slice #3 and deferred to **#3b**.

**Questions answered:** (1) which `OrderState` applies on failure, and (2) when a `DeliveryRoute` may transition to `COMPLETED`.

## Decision drivers

- **Operational pause with human action** — per `docs/states/order.md`, a new order state is justified when the order waits for a human decision.
- **Field reality** — drivers finish routes with partial success; blocking route closure until every failure is resolved adds friction and complicates liquidation (#4).
- **Bounded slice #3** — report failure + order pause + route close; no re-queue or cancel commands yet.
- **Event-driven boundaries** — Logistics emits facts; Orders updates its lifecycle; Billing is not wired to order states today.
- **Reversibility** — prefer choices that keep liquidation (#4) and resolution (#3b) implementable without rework.

## Considered options

### Decision 1 — Order on failed delivery

1. **1A — Re-queue** — `OUT_FOR_DELIVERY → READY_FOR_DELIVERY_ROUTE` automatically; Logistics upserts `DeliveryRouteCandidate(READY)`.
2. **1B — Operational pause** — new `DELIVERY_ATTEMPT_FAILED`; human resolves in #3b (`→ READY_FOR_DELIVERY_ROUTE` or `→ CANCELLED`).
3. **1C — Cancel** — `OUT_FOR_DELIVERY → CANCELLED`; stock released via existing `ORDER_CANCELLED` path.
4. **1D — No order change** — stop marked `FAILED` only; order stays `OUT_FOR_DELIVERY`.

### Decision 2 — Route closure with failed stops

1. **2A — Terminal stops → `COMPLETED`** — route closes when every stop is `DELIVERED | FAILED | SKIPPED`; summary via derived counters.
2. **2B — Block until resolved** — route stays `DISPATCHED` (or new intermediate state) while any stop is `FAILED`.
3. **2C — `COMPLETED_WITH_EXCEPTIONS`** — new `DeliveryRouteStatus` when any stop failed.

## Decision outcome

**Decision 1 — Chosen: 1B (`DELIVERY_ATTEMPT_FAILED`).** Failed delivery is an operational exception that requires a human decision before retry or cancel. Slice #3 only performs the transition into this pause; resolution ships in #3b.

**Decision 2 — Chosen: 2A (terminal stops → `COMPLETED`).** Route closure uses existing `COMPLETED` status when no stop remains non-terminal. UI and read APIs expose derived counts (`delivered`, `failed`, `skipped`) rather than a new route enum.

### Positive consequences

- Clear ops queue: orders in `DELIVERY_ATTEMPT_FAILED` are searchable and actionable in #3b.
- Drivers can close routes the same day; liquidation (#4) can target `COMPLETED` routes and filter stops by status.
- Minimal enum surface: one new `OrderState`, no new `DeliveryRouteStatus`.
- Aligns with `docs/states/order.md` guidance on meaningful pauses.

### Negative consequences

- Slice #3 does not unblock failed orders — ops must wait for #3b.
- `COMPLETED` alone does not signal “all green”; UI must show failure counts.
- Prisma migration + `ORDER_TRANSITIONS` update required for the new order state.

### Trade-offs accepted

- **1A discarded** — conflates “never dispatched” with “failed on attempt”; skips explicit review queue.
- **1C discarded** — too aggressive for retryable failures (absent customer, wrong address); hard to reverse commercially.
- **1D discarded** — order state lies to panel, billing prep, and downstream consumers.
- **2B discarded** — blocks driver end-of-day and makes liquidation (#4) depend on pre-resolution workflows.
- **2C discarded** — extra enum and filters for little gain over derived stop counters on `COMPLETED`.

## Pros and cons of the options

### 1A — Re-queue automatically

- **Good:** No new `OrderState`; reuses dispatch/candidate flow.
- **Bad:** Semantic overload on `READY_FOR_DELIVERY_ROUTE`; no explicit exception queue.

### 1B — `DELIVERY_ATTEMPT_FAILED` (chosen)

- **Good:** Honest pause; fits state-machine rules; clean handoff to #3b.
- **Bad:** Extra state, transitions, and UI filters; #3 alone leaves orders stuck until #3b.

### 1C — Cancel on failure

- **Good:** Reuses `CancelOrderCommand` side effects (inventory release).
- **Bad:** Kills retry without a new order; wrong default for transient failures.

### 1D — No order state change

- **Good:** Smallest Orders diff in #3.
- **Bad:** Operational lie; breaks trust in order list and future billing hooks.

### 2A — Terminal stops → `COMPLETED` (chosen)

- **Good:** Simple closure rule; shared by confirm and report-failure; #4-friendly.
- **Bad:** “Completed” requires UI context (badges/counts).

### 2B — Block route until failures resolved

- **Good:** Forces dispatcher attention before close.
- **Bad:** Zombie `DISPATCHED` routes; complicates liquidation and driver UX.

### 2C — `COMPLETED_WITH_EXCEPTIONS`

- **Good:** Explicit reporting dimension at route level.
- **Bad:** More migration and API surface; redundant with stop-level status.

## Validation

- After #3: reporting failure on a `PLANNED` stop of a `DISPATCHED` route sets stop `FAILED`, order `DELIVERY_ATTEMPT_FAILED`, and completes the route when all other stops are terminal.
- Orders in `DELIVERY_ATTEMPT_FAILED` appear in order list/detail; no accidental transition to `DELIVERED` or `SETTLED`.
- Liquidation (#4) can list `COMPLETED` routes and exclude `FAILED` stops from COD reconciliation without new route enums.
- Re-evaluation if: tenants need mandatory dispatcher sign-off before route close (→ reconsider 2B/2C), or retry rate makes 1A preferable over 1B.

## Resolution (#3b) — implemented

Slice **#3b** closes the loop opened by decision 1B:

| Action               | Command / endpoint                                                            | Order transition                                     | Side effects                                                                                                                                     |
| -------------------- | ----------------------------------------------------------------------------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Requeue** (manual) | `RequeueFailedDeliveryOrderCommand` · `POST /orders/:id/requeue-for-delivery` | `DELIVERY_ATTEMPT_FAILED → READY_FOR_DELIVERY_ROUTE` | Emits `ORDER_READY_FOR_DELIVERY_ROUTE`; Logistics resets existing `DeliveryRouteCandidate` from `ASSIGNED → READY` and clears `deliveryRouteId`. |
| **Cancel** (manual)  | `CancelOrderCommand` · `POST /orders/:id/cancel`                              | `DELIVERY_ATTEMPT_FAILED → CANCELLED`                | Emits `ORDER_CANCELLED`; Inventory releases stock; Logistics marks candidate `CANCELLED`.                                                        |

**Retry policy (v1):** unlimited requeues; no `retryCount` field. Operators decide; audit trail via `OrderTransition`. Future tenant policy may cap retries without schema change (count transitions).

## More information

- Related docs: [`docs/states/order.md`](../states/order.md), [`docs/domains/logistics.md`](../domains/logistics.md), [`docs/domains/orders.md`](../domains/orders.md)
- Follow-up slice: **#4** — route liquidation on `COMPLETED` routes
