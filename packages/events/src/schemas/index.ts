import type { z } from 'zod';

import { DomainEventName } from '../registry';

import { inventoryReleasedPayload } from './inventory-released';
import { inventoryReservationFailedPayload } from './inventory-reservation-failed';
import { inventoryReservedPayload } from './inventory-reserved';
import { orderApprovedPayload } from './order-approved';
import { orderCancelledPayload } from './order-cancelled';
import { orderCreatedPayload } from './order-created';
import { orderPickingStartedPayload } from './order-picking-started';
import { paymentRegisteredPayload } from './payment-registered';
import { pickingCompletedPayload } from './picking-completed';
import { saleCreatedPayload } from './sale-created';
import { userRegisteredPayload } from './user-registered';

export * from './inventory-released';
export * from './inventory-reservation-failed';
export * from './inventory-reserved';
export * from './order-approved';
export * from './order-cancelled';
export * from './order-created';
export * from './order-picking-started';
export * from './picking-completed';
export * from './payment-registered';
export * from './sale-created';
export * from './user-registered';

export const EventPayloadSchemas = {
  [DomainEventName.USER_REGISTERED]: userRegisteredPayload,
  [DomainEventName.ORDER_CREATED]: orderCreatedPayload,
  [DomainEventName.ORDER_APPROVED]: orderApprovedPayload,
  [DomainEventName.ORDER_CANCELLED]: orderCancelledPayload,
  [DomainEventName.INVENTORY_RESERVED]: inventoryReservedPayload,
  [DomainEventName.INVENTORY_RESERVATION_FAILED]: inventoryReservationFailedPayload,
  [DomainEventName.INVENTORY_RELEASED]: inventoryReleasedPayload,
  [DomainEventName.ORDER_PICKING_STARTED]: orderPickingStartedPayload,
  [DomainEventName.PICKING_COMPLETED]: pickingCompletedPayload,
  [DomainEventName.SALE_CREATED]: saleCreatedPayload,
  [DomainEventName.PAYMENT_REGISTERED]: paymentRegisteredPayload,
} as const;

export type EventPayloadFor<TName extends DomainEventName> = z.infer<
  (typeof EventPayloadSchemas)[TName]
>;
