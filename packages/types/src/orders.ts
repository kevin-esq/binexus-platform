// Order state machine — fully defined here so it stays in sync across backend, web, and sdk.
// See docs/states/order.md for the canonical reference.

import type { BranchId, ISODateString, OrderId, UserId } from './common';

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

export interface OrderLineSummary {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
  lineTotalCents: number;
}

export interface OrderTransitionSummary {
  id: string;
  fromState: OrderState | null;
  toState: OrderState;
  reason: string | null;
  occurredAt: ISODateString;
  byUserId: UserId;
}

export interface OrderSummary {
  id: OrderId;
  branchId: BranchId;
  customerId: string;
  state: OrderState;
  totalCents: number;
  currency: string;
  createdAt: ISODateString;
  lineCount: number;
}

export interface OrderDetail extends OrderSummary {
  createdByUserId: UserId;
  updatedAt: ISODateString;
  lines: OrderLineSummary[];
  transitions: OrderTransitionSummary[];
}

export interface ListOrdersQuery {
  limit?: number;
  cursor?: string;
}

export interface ListOrdersResult {
  items: OrderSummary[];
  nextCursor: string | null;
}

export interface ApproveOrderResult {
  id: OrderId;
  state: typeof OrderState.APPROVED;
}

export interface CancelOrderResult {
  id: OrderId;
  state: typeof OrderState.CANCELLED;
}
