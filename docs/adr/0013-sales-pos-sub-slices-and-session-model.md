# ADR-0013: Sales / POS — business scope, sub-slice order, and session-first retail model

| Field    | Value                                                          |
| -------- | -------------------------------------------------------------- |
| Status   | Accepted                                                       |
| Date     | 2026-07-10                                                     |
| Deciders | Kevin Esquivel                                                 |
| Tags     | sales, pos, f5, cash, session, orders, inventory, events, rbac |

## Context and problem statement

Phase **F5 · Sales / POS** introduces a new bounded context (`sales`) with a different operational shape than F4 Logistics: **mandatory cash-register sessions**, mostly **anonymous walk-in sales**, and (in the full vision) **split payments**, **B2B credit**, and **POS-originated delivery orders** that feed Orders → Warehouse → Logistics.

Today:

- `catalog` and `customers` contexts remain **stubs**; Sales is **active from slice 5.1**.
- Event schemas exist for `SALE_CREATED` and `PAYMENT_REGISTERED`; **5.1 produces both**.
- Inventory decrements stock inline on `CreateSaleCommand` (`StockMovementType.SALE`); event handler deferred.
- Orders already use **line snapshots** (`productId`, `productName`, price) and a required `customerId` string — a pattern POS mirrors without building Catalog/Customers first.
- F4 **route liquidation** (ADR-0012) established **operational cash arqueo**: expected vs. declared, supervisor + reason on discrepancy — reused for **session close** in 5.1.
- Feature flags `POS_RETAIL` and `POS_RESTAURANT` exist in seed (default off); desktop Tauri is scaffold-only; offline sync remains contracts-only (ADR-0003).

**Business decisions locked for F5:**

1. **Channel** — POS must eventually create **delivery orders** (Warehouse/Logistics), not only over-the-counter instant sales.
2. **Session** — **Cannot sell without opening a register session** with an **opening float** (fondo inicial).
3. **Customer** — **Anonymous / walk-in is the norm**; identified customer is exceptional.
4. **Credit** — **B2B credit sales at POS** are required in the full F5 scope.
5. **Split payment** — A single ticket may be paid with **multiple methods** from v1 of the **full** POS (not necessarily slice 5.1).

Implementing all five at once would mirror the mistake of shipping F4 liquidation before dispatch. F5 is therefore **sub-sliced** like F4.

**Questions answered:** (1) how F5 is phased, (2) what 5.1 deliberately excludes, (3) how session close relates to liquidation arqueo, (4) how Sales stays separate from Orders until 5.4, (5) how multi-register branches work (terminal-scoped sessions).

## Decision drivers

- **Foundation wide, execution narrow** — one demonstrable vertical per sub-slice; extensions do not block the previous slice's demo.
- **Operational cash discipline** — no selling without session; close with arqueo before the cashier leaves (same accountability culture as route liquidation).
- **Anonymous retail default** — avoid blocking 5.1 on Customers CRUD; snapshots and a walk-in sentinel suffice until credit (5.3) needs identified buyers.
- **Event boundaries** — Sales emits facts (`SALE_CREATED`, `PAYMENT_REGISTERED`); Inventory decrements stock in the sale transaction; Orders stay untouched until 5.4.
- **Reuse proven arqueo semantics** — expected vs. declared, supervisor gate on mismatch (ADR-0012 G3); 5.1 uses **route-total style** only (no per-line breakdown on discrepancy).
- **Feature flags** — `POS_RETAIL` gates retail POS; `POS_RESTAURANT` deferred.
- **Online-first for 5.1** — web `/pos`; Tauri/offline per ADR-0003 contracts, not implementation in 5.1.
- **Multi-register branches** — one OPEN session per **terminal** (register), not per branch.

## Considered options

### Phasing F5

1. **Monolith F5** — session + sale + split + credit + delivery orders in one PR.
2. **Sub-slices 5.1 → 5.4** — session + simple cash sale first; extensions in order (chosen).
3. **Session last** — ship anonymous cash sale without session, add session later.

### Session scope (5.1)

1. **One OPEN session per branch** — blocks a second cashier on the same branch (rejected).
2. **One OPEN session per `(tenantId, branchId, terminalId)`** — `terminalId` is a free-form string label (e.g. "Caja 1"); no Terminal catalog in 5.1 (chosen).
3. **Terminal catalog first** — admin-managed registers before any sale (deferred).

### Session close arqueo (5.1)

1. **S1 — No arqueo on close** — session closes on button only; cash variance unknown.
2. **S2 — Simple arqueo** — `expectedClosingCents = openingFloat + cashSales`; `declaredClosingCents` from cashier; discrepancy → supervisor + reason (chosen).
3. **S3 — Full B3 hybrid** — per-ticket line breakdown on mismatch (defer to later sub-slice if needed).

### Anonymous customer (5.1)

1. **C1 — Require `customerId` on every ticket** — forces Customers module early.
2. **C2 — Walk-in sentinel snapshot** — fixed `walk-in` label on ticket; no Customers row (chosen for 5.1).
3. **C3 — Nullable customer** — optional FK later.

### Delivery orders from POS (full F5)

1. **D1 — POS creates `Order` via event/command in 5.4** — Sales emits fact; Orders owns lifecycle (chosen for 5.4).
2. **D2 — POS duplicates order tables** — rejected (boundary violation).
3. **D3 — POS only uses existing Orders UI** — no POS integration (rejected per business decision 1).

## Decision outcome

### F5 sub-slice order (chosen: phased 5.1 → 5.4)

| Sub-slice | Scope                                                                                                                                                 | Out of scope (defer)                                                                            |
| --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| **5.1**   | `OpenSalesSession` (opening float, **terminal label**) → **anonymous cash sale** (single `CASH` payment) → `CloseSalesSession` with **simple arqueo** | Split, credit, delivery orders, restaurant, offline/Tauri, void/returns, Terminal admin catalog |
| **5.2**   | **Split payment** — multiple `PaymentCapture` rows per ticket                                                                                         | Credit, delivery orders                                                                         |
| **5.3**   | **B2B credit** at POS — identified customer snapshot, `CREDIT` capture, Billing hook prep                                                             | Delivery orders                                                                                 |
| **5.4**   | **POS → Order** for delivery — ticket or command creates `Order` entering Warehouse/Logistics                                                         | Restaurant, fiscal invoicing                                                                    |

**Rationale for this order:**

- **5.1** proves the new context, session gate, inventory decrement, and arqueo without payment-method combinatorics.
- **5.2** only changes payment capture shape; session and sale flow stay stable.
- **5.3** introduces identified customer + credit policy — depends on business rules Customers would own, but can start with snapshot + flag before full Customers module.
- **5.4** is the largest cross-context integration; it requires a stable POS sale path and benefits from settled payment semantics from 5.2/5.3.

### 5.1 scope detail (chosen)

| Area             | Decision                                                                                                                                                                                                                                                           |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Session**      | At most **one `OPEN` session per `(tenantId, branchId, terminalId)`**; `terminalId` is a string label entered at open (no Terminal entity in 5.1); `OpenSalesSessionCommand` with `openingFloatCents`; cannot `CreateSale` without OPEN session for that terminal. |
| **Sale**         | `CreateSaleCommand` — atomic ticket + lines + single `CASH` `PaymentCapture`; walk-in snapshot only; manual line pricing (snapshot, like Orders).                                                                                                                  |
| **Stock**        | Inline decrement in sale transaction (`StockMovementType.SALE`); reject sale if `available < quantity` (all-or-nothing per line).                                                                                                                                  |
| **Close**        | `CloseSalesSessionCommand` — `expectedClosingCents = openingFloatCents + sum(cash sales in session)`; `declaredClosingCents` required; mismatch → **G3-style** supervisor (`ADMIN` \| `SUPER_ADMIN`) + `discrepancyReason` (no per-ticket breakdown in 5.1).       |
| **Events**       | Register and emit `SALES_SESSION_OPENED`, `SALES_SESSION_CLOSED`; extend `SALE_CREATED` payload; Sales produces `PAYMENT_REGISTERED`.                                                                                                                              |
| **Feature flag** | `@RequireFeature(POS_RETAIL)`.                                                                                                                                                                                                                                     |
| **UI**           | Web `/pos` only.                                                                                                                                                                                                                                                   |

### Positive consequences

- F5 ships incrementally with demoable milestones every sub-slice.
- Session + arqueo reuse operational patterns operators already learn from route liquidation.
- 5.1 avoids Catalog/Customers modules by reusing Inventory + snapshots (same as early Orders).
- Clear fence: instant retail in Sales; fulfillment pipeline untouched until 5.4.
- Multiple cashiers can sell concurrently on different terminals in the same branch.

### Negative consequences

- Two arqueo concepts in the product (route liquidation vs. session close) — must document clearly for ops.
- Walk-in sentinel is a bridge until Customers exists; 5.3 may require refactor to real customer snapshots.
- `SALE_CREATED` schema extended with line detail (breaking for schema-only consumers).
- Free-form `terminalId` strings may collide or drift until a Terminal catalog slice.

### Trade-offs accepted

- **Monolith F5 discarded** — too large to review, test, and demo safely.
- **Session last discarded** — violates business rule “no sell without open register.”
- **Session-per-branch discarded** — blocks multi-register operations.
- **S1 / S3 for 5.1 discarded** — S1 skips accountability; S3 is liquidation-grade UX not needed for first cashier close.
- **Restaurant (`POS_RESTAURANT`)** — explicit defer past 5.4 planning.

## Pros and cons of the options

### Sub-slices 5.1 → 5.4 (chosen)

- **Good:** Matches F4 success pattern; each PR is reviewable; business rules land in order of dependency.
- **Bad:** More ADR/docs/Notion churn; temporary product limitations between slices.

### Monolith F5

- **Good:** Single “POS done” moment.
- **Bad:** High regression risk; blocks learning on session vs. payment vs. order integration separately.

## Validation

- **5.1:** Cashier cannot `CreateSale` without OPEN session for their terminal; cannot open second OPEN session on same `(branch, terminal)`; two terminals on same branch can both sell; sale reduces inventory; session close with matching declared amount succeeds without supervisor; mismatch without `ADMIN` → `403`; with reason → closes and persists discrepancy.
- **5.2:** Ticket total equals sum of payment captures; mixed methods allowed.
- **5.3:** Credit ticket creates no immediate cash in session expected total; identified customer snapshot on ticket.
- **5.4:** Delivery sale creates `Order` in `DRAFT` or approved path per separate ADR section; enters existing F2–F4 pipeline.
- Re-evaluation if: offline session sync, fiscal Z-report integration, or Billing requires merging session close with fiscal close.

## More information

- Related ADRs: [ADR-0012](0012-route-liquidation-cod-reconciliation.md), [ADR-0003](0003-offline-first-design.md), [ADR-0009](0009-feature-flags-tenant-scoped.md), [ADR-0006](0006-authentication-jwt-argon2-rbac.md)
- Related docs: [`docs/domains/sales.md`](../domains/sales.md), [`docs/domains/inventory.md`](../domains/inventory.md), [`docs/domains/orders.md`](../domains/orders.md)
