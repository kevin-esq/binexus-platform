# Workflow: order creation → ready for delivery route

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
    Inventory->>Bus: emit INVENTORY_RESERVED (or INVENTORY_RESERVATION_FAILED)
    Bus->>OrdersAPI: INVENTORY_RESERVATION_FAILED -> auto-cancel APPROVED order
    Client->>OrdersAPI: AssignWarehouseCommand(orderId)
    OrdersAPI->>OrdersAPI: transition APPROVED -> PICKING
    OrdersAPI->>Bus: emit ORDER_PICKING_STARTED
    Bus->>Warehouse: ORDER_PICKING_STARTED -> create PickingTask
    Warehouse->>Warehouse: warehouse staff complete picking
    Warehouse->>Bus: emit PICKING_COMPLETED
    Bus->>OrdersAPI: PICKING_COMPLETED -> transition PICKING -> READY_FOR_DELIVERY_ROUTE
```

## Steps in plain language

1. **Create** — `Orders` writes the order in `DRAFT`, emits `ORDER_CREATED`.
2. **Validate credit** — synchronous inside `Orders`, applies only to B2B tenants (open question to resolve at Phase 1 kickoff).
3. **Approve** — `Orders` transitions to `APPROVED`, emits `ORDER_APPROVED`.
4. **Reserve stock** — **implemented (slice):** `Inventory` reacts to `ORDER_APPROVED` (after outbox dispatch), reserves stock in a transaction, and emits `INVENTORY_RESERVED` or `INVENTORY_RESERVATION_FAILED` via outbox. No partial reservation; idempotent per order line.
5. **Compensate on failure** — **implemented:** `Orders` reacts to `INVENTORY_RESERVATION_FAILED` and auto-cancels orders still in `APPROVED` (`APPROVED -> CANCELLED` via `CancelOrderCommand`, actor: tenant `system` user). Idempotent on re-delivery.
6. **Start picking** — **implemented:** `Orders` consumes `INVENTORY_RESERVED`, transitions `APPROVED -> PICKING`, emits `ORDER_PICKING_STARTED`.
7. **Picking** — **implemented:** `Warehouse` consumes `ORDER_PICKING_STARTED`, creates `PickingTask` + lines; `CompletePickingTaskCommand` emits `PICKING_COMPLETED`.
8. **Ready for delivery route** — **implemented:** `Orders` consumes `PICKING_COMPLETED`, transitions `PICKING -> READY_FOR_DELIVERY_ROUTE`, emits `ORDER_READY_FOR_DELIVERY_ROUTE`.
9. **Route planning** — **implemented:** `Logistics` consumes `ORDER_READY_FOR_DELIVERY_ROUTE`, projects `DeliveryRouteCandidate`. Dispatcher creates `DeliveryRoute(PLANNED)` and assigns candidates via `AssignOrderToDeliveryRouteCommand` (`DELIVERY_ROUTE_CREATED`, `DELIVERY_ROUTE_ASSIGNED`). Dispatch is the next slice.

## Cross-context contracts implied by this flow

- `Orders` consumes `INVENTORY_RESERVATION_FAILED` (**active** — auto-cancel), `INVENTORY_RESERVED` (**active** — auto picking), `PICKING_COMPLETED` (**active** — ready for delivery route).
- `Orders` emits `ORDER_CREATED`, `ORDER_APPROVED`, `ORDER_CANCELLED`, `ORDER_PICKING_STARTED`, `ORDER_READY_FOR_DELIVERY_ROUTE` (**active**).
- `Logistics` consumes `ORDER_READY_FOR_DELIVERY_ROUTE` (**active**).
- `Logistics` emits `DELIVERY_ROUTE_CREATED`, `DELIVERY_ROUTE_ASSIGNED` (**active**).
- `Inventory` consumes `ORDER_APPROVED`, `ORDER_CANCELLED` (**active**).
- `Inventory` emits `INVENTORY_RESERVED`, `INVENTORY_RESERVATION_FAILED`, `INVENTORY_RELEASED` (**active**).
- **Cancel path (slice):** `ORDER_CANCELLED` triggers `INVENTORY_RELEASED` when active reservations exist.
- `Warehouse` consumes `ORDER_PICKING_STARTED` (**active**).
- `Warehouse` emits `PICKING_COMPLETED` (**active**).

Each of those events needs a Zod schema in `packages/events/src/schemas/` before its producer ships.

## Idempotency

- All command endpoints accept an `Idempotency-Key` header. Replays with the same key return the same result without re-executing side effects.
- All event handlers are idempotent on `event.id` (store the last processed `event.id` per consumer / per aggregate).
