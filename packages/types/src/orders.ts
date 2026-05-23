// Order state machine — fully defined here so it stays in sync across backend, web, and sdk.
// See docs/states/order.md for the canonical reference.

export const OrderState = {
  DRAFT: 'DRAFT',
  APPROVED: 'APPROVED',
  PICKING: 'PICKING',
  READY_FOR_ROUTE: 'READY_FOR_ROUTE',
  OUT_FOR_DELIVERY: 'OUT_FOR_DELIVERY',
  DELIVERED: 'DELIVERED',
  SETTLED: 'SETTLED',
  CANCELLED: 'CANCELLED',
} as const;

export type OrderState = (typeof OrderState)[keyof typeof OrderState];

export const ORDER_TRANSITIONS: Readonly<Record<OrderState, readonly OrderState[]>> = {
  DRAFT: ['APPROVED', 'CANCELLED'],
  APPROVED: ['PICKING', 'CANCELLED'],
  PICKING: ['READY_FOR_ROUTE'],
  READY_FOR_ROUTE: ['OUT_FOR_DELIVERY'],
  OUT_FOR_DELIVERY: ['DELIVERED'],
  DELIVERED: ['SETTLED'],
  SETTLED: [],
  CANCELLED: [],
};

export function canTransition(from: OrderState, to: OrderState): boolean {
  return ORDER_TRANSITIONS[from].includes(to);
}
