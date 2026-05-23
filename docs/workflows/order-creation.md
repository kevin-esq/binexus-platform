# Workflow: order creation → ready for route

The end-to-end flow we will implement in Phase 1. Documents the choreography between bounded contexts so we keep contracts honest **before** writing code.

```mermaid
sequenceDiagram
    autonumber
    participant Client
    participant OrdersAPI as Orders context
    participant Identity as Identity context
    participant Inventory as Inventory context
    participant Warehouse as Warehouse context
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
    Bus->>Warehouse: ORDER_PICKING_STARTED -> create PickingTask
    Warehouse->>Warehouse: warehouse staff complete picking
    Warehouse->>Bus: emit PICKING_COMPLETED
    Bus->>OrdersAPI: PICKING_COMPLETED -> transition PICKING -> READY_FOR_ROUTE
```

## Steps in plain language

1. **Create** — `Orders` writes the order in `DRAFT`, emits `ORDER_CREATED`.
2. **Validate credit** — synchronous inside `Orders`, applies only to B2B tenants (open question to resolve at Phase 1 kickoff).
3. **Approve** — `Orders` transitions to `APPROVED`, emits `ORDER_APPROVED`.
4. **Reserve stock** — `Inventory` reacts to `ORDER_APPROVED`, atomically reserves stock and emits either success or failure.
5. **Compensate on failure** — `Orders` reacts to `STOCK_RESERVATION_FAILED` and either reverts to `DRAFT` or cancels (rule TBD per tenant).
6. **Assign warehouse** — `Orders` transitions to `PICKING`, emits `ORDER_PICKING_STARTED`.
7. **Picking** — `Warehouse` creates a picking task. When complete, it emits `PICKING_COMPLETED`.
8. **Ready for route** — `Orders` reacts to `PICKING_COMPLETED` and transitions to `READY_FOR_ROUTE`. From here the Logistics workflow takes over.

## Cross-context contracts implied by this flow

- `Orders` consumes `STOCK_RESERVATION_FAILED`, `STOCK_RESERVED`, `PICKING_COMPLETED`.
- `Orders` emits `ORDER_CREATED`, `ORDER_APPROVED`, `ORDER_CANCELLED`, `ORDER_PICKING_STARTED`.
- `Inventory` consumes `ORDER_APPROVED`, `ORDER_CANCELLED`.
- `Inventory` emits `STOCK_RESERVED`, `STOCK_RESERVATION_FAILED`, `STOCK_RELEASED`.
- `Warehouse` consumes `ORDER_PICKING_STARTED`.
- `Warehouse` emits `PICKING_COMPLETED`.

Each of those events needs a Zod schema in `packages/events/src/schemas/` before its producer ships.

## Idempotency

- All command endpoints accept an `Idempotency-Key` header. Replays with the same key return the same result without re-executing side effects.
- All event handlers are idempotent on `event.id` (store the last processed `event.id` per consumer / per aggregate).
