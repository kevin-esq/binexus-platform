# Event catalog

Every domain event in the platform. Schemas live in [`packages/events/src/schemas/`](../../packages/events/src/schemas/) and are the source of truth — this table is a quick reference.

## Registered events

| Event                | Producer   | Consumers                | Schema                                                                             |
| -------------------- | ---------- | ------------------------ | ---------------------------------------------------------------------------------- |
| `USER_REGISTERED`    | `identity` | (audit only in F0)       | [`user-registered.ts`](../../packages/events/src/schemas/user-registered.ts)       |
| `ORDER_CREATED`      | `orders`   | audit / reporting (F8+)  | [`order-created.ts`](../../packages/events/src/schemas/order-created.ts)           |
| `ORDER_APPROVED`     | `orders`\* | `inventory` (F2)         | [`order-approved.ts`](../../packages/events/src/schemas/order-approved.ts)         |
| `ORDER_CANCELLED`    | `orders`\* | `inventory` (F2)         | [`order-cancelled.ts`](../../packages/events/src/schemas/order-cancelled.ts)       |
| `SALE_CREATED`       | `sales`\*  | `inventory`, `billing`\* | [`sale-created.ts`](../../packages/events/src/schemas/sale-created.ts)             |
| `PAYMENT_REGISTERED` | `sales`\*  | `billing`\*              | [`payment-registered.ts`](../../packages/events/src/schemas/payment-registered.ts) |

`*` = schema registered, producer not implemented until the marked phase.

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

Persisted in the `OutboxEvent` table within the same transaction as the originating command.

## How to add an event

1. Add the name to `DomainEventName` in [`packages/events/src/registry.ts`](../../packages/events/src/registry.ts).
2. Create a Zod schema at `packages/events/src/schemas/<name>.ts`.
3. Wire it into `packages/events/src/schemas/index.ts → EventPayloadSchemas`.
4. Add a row to the table above.
5. In the producer: call `eventBus.build(...)` and `outbox.record(...)` inside the same DB transaction as the state change.
6. In the consumer(s): use `@OnEvent('NAME')` on a handler method.

## Versioning

- Bumping `version` is a breaking change.
- A breaking change requires either a synchronized consumer update or a dual-publish window (publish v1 and v2 simultaneously while consumers migrate).
- Document the migration in this file and in `docs/architecture/event-system.md`.
