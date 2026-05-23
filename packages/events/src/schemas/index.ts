import type { z } from 'zod';

import { DomainEventName } from '../registry';

import { orderApprovedPayload } from './order-approved';
import { orderCancelledPayload } from './order-cancelled';
import { orderCreatedPayload } from './order-created';
import { paymentRegisteredPayload } from './payment-registered';
import { saleCreatedPayload } from './sale-created';
import { userRegisteredPayload } from './user-registered';

export * from './order-approved';
export * from './order-cancelled';
export * from './order-created';
export * from './payment-registered';
export * from './sale-created';
export * from './user-registered';

export const EventPayloadSchemas = {
  [DomainEventName.USER_REGISTERED]: userRegisteredPayload,
  [DomainEventName.ORDER_CREATED]: orderCreatedPayload,
  [DomainEventName.ORDER_APPROVED]: orderApprovedPayload,
  [DomainEventName.ORDER_CANCELLED]: orderCancelledPayload,
  [DomainEventName.SALE_CREATED]: saleCreatedPayload,
  [DomainEventName.PAYMENT_REGISTERED]: paymentRegisteredPayload,
} as const;

export type EventPayloadFor<TName extends DomainEventName> = z.infer<
  (typeof EventPayloadSchemas)[TName]
>;
