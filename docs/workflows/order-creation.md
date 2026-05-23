# Workflow: order creation → ready for route

The end-to-end flow we will implement in Phase 1. Documents the choreography between bounded contexts so we keep contracts honest **before** writing code.

```mermaid
sequenceDiagram
    autonumber
    participant Client
    participant OrdersAPI as Orders context
    participant Identity as Identity context
    participant Inventory as Inventory context
    participant Logistics as Logistics context
    participant Bus as Event bus

    Client->>OrdersAPI: CreateOrderCommand(input)
    OrdersAPI->>Identity: lookup user / branch (allowed direct read)
    OrdersAPI->>OrdersAPI: validate credit (B2B only)
    OrdersAPI->>OrdersAPI: persist Order in DRAFT
    OrdersAPI->>Bus: emit ORDER_CREATED (via outbox)
    Client->>OrdersAPI: ApproveOrderCommand(orderId)
    OrdersAPI->>OrdersAPI: transition DRAFT -> APPROVED
    OrdersAPI->>Bus: emit ORDER_APPROVED
    Bus->>Inventory: ORDER_APPROVED
    Inventory->>Inventory: reserve stock (StockReservation)
    Inventory->>Bus: emit STOCK_RESERVED (or STOCK_RESERVATION_FAILED)
    Bus->>OrdersAPI: STOCK_RESERVATION_FAILED -> rollback to DRAFT or CANCELLED
    Client->>OrdersAPI: AssignWarehouseCommand(orderId)
    OrdersAPI->>OrdersAPI: transition APPROVED -> PICKING
    OrdersAPI->>Bus: emit ORDER_PICKING_STARTED
    Bus->>Logistics: ORDER_PICKING_STARTED -> create PickingTask
    Logistics->>Logistics: warehouse staff complete picking
    Logistics->>Bus: emit ORDER_PICKED
    Bus->>OrdersAPI: ORDER_PICKED -> transition PICKING -> READY_FOR_ROUTE
```

## Steps in plain language

1. **Create** — `Orders` writes the order in `DRAFT`, emits `ORDER_CREATED`.
2. **Validate credit** — synchronous inside `Orders`, applies only to B2B tenants (open question to resolve at Phase 1 kickoff).
3. **Approve** — `Orders` transitions to `APPROVED`, emits `ORDER_APPROVED`.
4. **Reserve stock** — `Inventory` reacts to `ORDER_APPROVED`, atomically reserves stock and emits either success or failure.
5. **Compensate on failure** — `Orders` reacts to `STOCK_RESERVATION_FAILED` and either reverts to `DRAFT` or cancels (rule TBD per tenant).
6. **Assign warehouse** — `Orders` transitions to `PICKING`, emits `ORDER_PICKING_STARTED`.
7. **Picking** — `Logistics` creates a picking task. When complete, it emits `ORDER_PICKED`.
8. **Ready for route** — `Orders` reacts to `ORDER_PICKED` and transitions to `READY_FOR_ROUTE`. From here the Routes workflow takes over.

## Cross-context contracts implied by this flow

- `Orders` consumes `STOCK_RESERVATION_FAILED`, `STOCK_RESERVED`, `ORDER_PICKED`.
- `Orders` emits `ORDER_CREATED`, `ORDER_APPROVED`, `ORDER_CANCELLED`, `ORDER_PICKING_STARTED`.
- `Inventory` consumes `ORDER_APPROVED`, `ORDER_CANCELLED`.
- `Inventory` emits `STOCK_RESERVED`, `STOCK_RESERVATION_FAILED`, `STOCK_RELEASED`.
- `Logistics` consumes `ORDER_PICKING_STARTED`.
- `Logistics` emits `ORDER_PICKED`.

Each of those events needs a Zod schema in `packages/events/src/schemas/` before its producer ships.

## Idempotency

- All command endpoints accept an `Idempotency-Key` header. Replays with the same key return the same result without re-executing side effects.
- All event handlers are idempotent on `event.id` (store the last processed `event.id` per consumer / per aggregate).
