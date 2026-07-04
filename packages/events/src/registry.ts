// Central registry of every domain event name in the platform.
// Add a new event:
//   1. Add a key here.
//   2. Add a Zod schema in ./schemas/<event>.ts.
//   3. Wire it into ./schemas/index.ts -> EventSchemas.
//   4. Document it in docs/events/README.md.

export const DomainEventName = {
  // Identity
  USER_REGISTERED: 'USER_REGISTERED',

  // Orders (F1)
  ORDER_CREATED: 'ORDER_CREATED',
  ORDER_APPROVED: 'ORDER_APPROVED',
  ORDER_CANCELLED: 'ORDER_CANCELLED',

  // Inventory (F2 — active)
  INVENTORY_RESERVED: 'INVENTORY_RESERVED',
  INVENTORY_RESERVATION_FAILED: 'INVENTORY_RESERVATION_FAILED',
  INVENTORY_RELEASED: 'INVENTORY_RELEASED',

  // Orders / Warehouse (F3 — active)
  ORDER_PICKING_STARTED: 'ORDER_PICKING_STARTED',
  PICKING_COMPLETED: 'PICKING_COMPLETED',
  ORDER_READY_FOR_DELIVERY_ROUTE: 'ORDER_READY_FOR_DELIVERY_ROUTE',
  ORDER_DELIVERED: 'ORDER_DELIVERED',

  // Logistics (F4 — active)
  DELIVERY_ROUTE_CREATED: 'DELIVERY_ROUTE_CREATED',
  DELIVERY_ROUTE_ASSIGNED: 'DELIVERY_ROUTE_ASSIGNED',
  DELIVERY_ROUTE_DISPATCHED: 'DELIVERY_ROUTE_DISPATCHED',
  DELIVERY_CONFIRMED: 'DELIVERY_CONFIRMED',
  DELIVERY_FAILED: 'DELIVERY_FAILED',

  // Sales / POS (F5 — schema only)
  SALE_CREATED: 'SALE_CREATED',

  // Billing (F7 — schema only)
  PAYMENT_REGISTERED: 'PAYMENT_REGISTERED',
} as const;

export type DomainEventName = (typeof DomainEventName)[keyof typeof DomainEventName];

export const ALL_DOMAIN_EVENT_NAMES: DomainEventName[] = Object.values(DomainEventName);
