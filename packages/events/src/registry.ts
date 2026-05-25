// Central registry of every domain event name in the platform.
// Add a new event:
//   1. Add a key here.
//   2. Add a Zod schema in ./schemas/<event>.ts.
//   3. Wire it into ./schemas/index.ts -> EventSchemas.
//   4. Document it in docs/events/README.md.

export const DomainEventName = {
  // Identity
  USER_REGISTERED: 'USER_REGISTERED',

  // Orders (placeholders — implemented in Phase 1)
  ORDER_CREATED: 'ORDER_CREATED',
  ORDER_APPROVED: 'ORDER_APPROVED',
  ORDER_CANCELLED: 'ORDER_CANCELLED',

  // Inventory (Phase 2)
  INVENTORY_RESERVED: 'INVENTORY_RESERVED',
  INVENTORY_RESERVATION_FAILED: 'INVENTORY_RESERVATION_FAILED',
  INVENTORY_RELEASED: 'INVENTORY_RELEASED',

  // Sales / POS (Phase 5)
  SALE_CREATED: 'SALE_CREATED',

  // Billing (Phase 7)
  PAYMENT_REGISTERED: 'PAYMENT_REGISTERED',
} as const;

export type DomainEventName = (typeof DomainEventName)[keyof typeof DomainEventName];

export const ALL_DOMAIN_EVENT_NAMES: DomainEventName[] = Object.values(DomainEventName);
