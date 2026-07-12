# Event catalog

Every domain event in the platform. **Human catalog** for producers/consumers and notes.

## Source of truth (today)

| Layer                       | Location                                                                                                                                                                                                                  | Role                                                                                                                                     |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| **Runtime contracts**       | Module producers/consumers under [`apps/backend/src/Modules/`](../../apps/backend/src/Modules/) + outbox envelope in [`apps/backend/src/Binexus.Platform/Messaging/`](../../apps/backend/src/Binexus.Platform/Messaging/) | Event names are string literals (`EventName` on `OutboxMessage` / `IIntegrationEventProcessor`); payloads are JSON staged with the write |
| **Selected JSON snapshots** | [`docs/events/schemas/`](./schemas/)                                                                                                                                                                                      | Review aids for a subset of payloads (not a complete registry)                                                                           |
| **This catalog**            | this file                                                                                                                                                                                                                 | Index of event names, producers, consumers                                                                                               |

There is **no** `apps/backend/contracts/events` directory today. Do not invent an empty folder for docs.

**Future (proposal only):** optional centralized versioned contracts under something like `apps/backend/contracts/events/` generated or hand-authored and shared by producers/consumers — not implemented in this PR.

`@binexus/events` (Zod under `packages/events`) was removed in [ADR-0015](../adr/0015-nestjs-retirement-dotnet-sole-backend.md).

**Backend:** C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL.

## Registered events

| Event                            | Producer                       | Consumers                                                                                                                                                                                                                                                     | Notes                                                                  |
| -------------------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| `USER_REGISTERED`                | `identity`                     | (audit only in F0)                                                                                                                                                                                                                                            | Runtime string in Identity module                                      |
| `ORDER_CREATED`                  | `orders`                       | `audit` (active), reporting (F8+)                                                                                                                                                                                                                             | Runtime string in Orders module                                        |
| `ORDER_APPROVED`                 | `orders`                       | `audit` (active), `inventory` (active), `warehouse` (active)                                                                                                                                                                                                  | Snapshot: [`order-approved.v1.json`](./schemas/order-approved.v1.json) |
| `ORDER_READY_FOR_DELIVERY_ROUTE` | `orders`                       | `logistics` (active) — Strategy A: READY branch update; ASSIGNED→READY reopen; CANCELLED skip                                                                                                                                                                 | Runtime string                                                         |
| `ORDER_CANCELLED`                | `orders`                       | `audit` (active), `inventory` (active), `logistics` (active)                                                                                                                                                                                                  | Runtime string                                                         |
| `INVENTORY_RESERVED`             | `inventory`                    | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `INVENTORY_RESERVATION_FAILED`   | historical Nest async producer | dormant/deprecated for synchronous approval; no .NET sync producer ([ADR-0014](../adr/0014-inventory-sync-reservation-and-tenant-middleware.md))                                                                                                              | historical                                                             |
| `INVENTORY_RELEASED`             | `inventory`                    | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `ORDER_PICKING_STARTED`          | `orders`                       | reporting / audit future                                                                                                                                                                                                                                      | Runtime string                                                         |
| `PICKING_COMPLETED`              | `warehouse`                    | reporting / audit future; Orders updated sync via contract                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_ROUTE_CREATED`         | `logistics`                    | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_ROUTE_ASSIGNED`        | `logistics`                    | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_ROUTE_DISPATCHED`      | `logistics`                    | reporting / audit future; Orders updated sync via contract                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_CONFIRMED`             | `logistics`                    | reporting / audit future; Orders updated sync via contract                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_FAILED`                | `logistics`                    | reporting / audit future; Orders updated sync via contract                                                                                                                                                                                                    | Runtime string                                                         |
| `DELIVERY_ROUTE_LIQUIDATED`      | `logistics`                    | reporting / audit future; Orders settled sync via contract                                                                                                                                                                                                    | Runtime string                                                         |
| `ORDER_DELIVERED`                | `orders`                       | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `ORDER_SETTLED`                  | `orders`                       | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `SALES_SESSION_OPENED`           | `sales`                        | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `SALES_SESSION_CLOSED`           | `sales`                        | (none yet)                                                                                                                                                                                                                                                    | Runtime string                                                         |
| `SALE_CREATED`                   | `sales`                        | stock via `IInventorySaleApi` in same TX; payload includes `saleId`/`ticketId` (same id), `sessionId`, `payments[]`. Outbox same TX as write; worker delivers per-aggregate order only — consumers must be order-tolerant and idempotent by event id / inbox. | Runtime string                                                         |
| `PAYMENT_REGISTERED`             | `sales`                        | `billing`\* (one event per `PaymentCapture`). Payload includes `saleId` + `sessionId`. Same TX as `SALE_CREATED`; global worker order not guaranteed vs sibling events.                                                                                       | Runtime string                                                         |
| `STOCK_SOLD`                     | `inventory` (sale decrement)   | (none yet). Informational physical-stock batch for a sale — distinct from commercial `SALE_CREATED`. Staged in the same TX as the POS sale.                                                                                                                   | Snapshot: [`stock-sold.v1.json`](./schemas/stock-sold.v1.json)         |

`*` on Billing = consumer not implemented until F7.

### Delivery / ordering notes (Sales)

- Outbox rows for `SALE_CREATED` + N× `PAYMENT_REGISTERED` (+ Inventory `STOCK_SOLD`) are inserted in the **same DB transaction** as the sale write.
- The outbox worker claims messages independently; **order is not globally guaranteed** across aggregates. Consumers must tolerate `PAYMENT_REGISTERED` before/after `SALE_CREATED` for the same `saleId`, and must treat redelivery as idempotent by envelope `id` / handler inbox.
- Do not remove `STOCK_SOLD` without a schema migration — it is a versioned v1 payload (`saleId`, optional `tenantId`, `branchId`, `lineCount`).

## Envelope

Every event is wrapped in:

```ts
interface DomainEvent<TName, TPayload> {
  id: string; // UUID — used for consumer idempotency
  name: TName;
  tenantId: string;
  occurredAt: string; // ISO 8601
  version: number; // bump on breaking change
  correlationId?: string;
  causationId?: string;
  payload: TPayload;
}
```

Persisted in the outbox table within the same EF transaction as the originating command. See [`OutboxMessage`](../../apps/backend/src/Binexus.Platform/Messaging/OutboxMessage.cs).

## How to add an event

1. Define the event name and payload shape in the **producing module** (and any typed payload records used when staging the outbox row).
2. Register consumers via `IIntegrationEventProcessor` / handler registry where needed.
3. Add a row to the table above; add a JSON snapshot under `docs/events/schemas/` when useful for review.
4. Stage the outbox row inside the same DB transaction as the state change; consumers must be idempotent by envelope `id`.

## Versioning

- Bumping `version` is a breaking change.
- A breaking change requires either a synchronized consumer update or a dual-publish window (publish v1 and v2 simultaneously while consumers migrate).
- Document the migration in this file and in `docs/architecture/event-system.md`.
