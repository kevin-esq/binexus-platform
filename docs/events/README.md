# Event catalog

Every domain event in the platform. Schemas live in [`packages/events/src/schemas/`](../../packages/events/src/schemas/) and are the source of truth — this table is a quick reference.

## Registered events

| Event                            | Producer    | Consumers                              | Schema                                                                                                     |
| -------------------------------- | ----------- | -------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `USER_REGISTERED`                | `identity`  | (audit only in F0)                     | [`user-registered.ts`](../../packages/events/src/schemas/user-registered.ts)                               |
| `ORDER_CREATED`                  | `orders`    | `audit` (active), reporting (F8+)      | [`order-created.ts`](../../packages/events/src/schemas/order-created.ts)                                   |
| `ORDER_APPROVED`                 | `orders`    | `audit` (active), `inventory` (active) | [`order-approved.ts`](../../packages/events/src/schemas/order-approved.ts)                                 |
| `ORDER_CANCELLED`                | `orders`    | `audit` (active), `inventory` (active) | [`order-cancelled.ts`](../../packages/events/src/schemas/order-cancelled.ts)                               |
| `INVENTORY_RESERVED`             | `inventory` | (none yet)                             | [`inventory-reserved.ts`](../../packages/events/src/schemas/inventory-reserved.ts)                         |
| `INVENTORY_RESERVATION_FAILED`   | `inventory` | `orders` (active)                      | [`inventory-reservation-failed.ts`](../../packages/events/src/schemas/inventory-reservation-failed.ts)     |
| `INVENTORY_RELEASED`             | `inventory` | (none yet)                             | [`inventory-released.ts`](../../packages/events/src/schemas/inventory-released.ts)                         |
| `ORDER_PICKING_STARTED`          | `orders`    | `warehouse` (active)                   | [`order-picking-started.ts`](../../packages/events/src/schemas/order-picking-started.ts)                   |
| `PICKING_COMPLETED`              | `warehouse` | `orders` (active)                      | [`picking-completed.ts`](../../packages/events/src/schemas/picking-completed.ts)                           |
| `ORDER_READY_FOR_DELIVERY_ROUTE` | `orders`    | `logistics` (active)                   | [`order-ready-for-delivery-route.ts`](../../packages/events/src/schemas/order-ready-for-delivery-route.ts) |
| `DELIVERY_ROUTE_CREATED`         | `logistics` | (none yet)                             | [`delivery-route-created.ts`](../../packages/events/src/schemas/delivery-route-created.ts)                 |
| `DELIVERY_ROUTE_ASSIGNED`        | `logistics` | (none yet)                             | [`delivery-route-assigned.ts`](../../packages/events/src/schemas/delivery-route-assigned.ts)               |
| `DELIVERY_ROUTE_DISPATCHED`      | `logistics` | `orders` (active)                      | [`delivery-route-dispatched.ts`](../../packages/events/src/schemas/delivery-route-dispatched.ts)           |
| `DELIVERY_CONFIRMED`             | `logistics` | `orders` (active)                      | [`delivery-confirmed.ts`](../../packages/events/src/schemas/delivery-confirmed.ts)                         |
| `DELIVERY_FAILED`                | `logistics` | `orders` (active)                      | [`delivery-failed.ts`](../../packages/events/src/schemas/delivery-failed.ts)                               |
| `ORDER_DELIVERED`                | `orders`    | (none yet)                             | [`order-delivered.ts`](../../packages/events/src/schemas/order-delivered.ts)                               |
| `SALE_CREATED`                   | `sales`\*   | `inventory`, `billing`\*               | [`sale-created.ts`](../../packages/events/src/schemas/sale-created.ts)                                     |
| `PAYMENT_REGISTERED`             | `sales`\*   | `billing`\*                            | [`payment-registered.ts`](../../packages/events/src/schemas/payment-registered.ts)                         |

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
