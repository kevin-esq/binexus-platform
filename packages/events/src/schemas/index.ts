import type { z } from 'zod';

import { DomainEventName } from '../registry';

import { deliveryConfirmedPayload } from './delivery-confirmed';
import { deliveryFailedPayload } from './delivery-failed';
import { deliveryRouteAssignedPayload } from './delivery-route-assigned';
import { deliveryRouteCreatedPayload } from './delivery-route-created';
import { deliveryRouteDispatchedPayload } from './delivery-route-dispatched';
import { deliveryRouteLiquidatedPayload } from './delivery-route-liquidated';
import { inventoryReleasedPayload } from './inventory-released';
import { inventoryReservationFailedPayload } from './inventory-reservation-failed';
import { inventoryReservedPayload } from './inventory-reserved';
import { orderApprovedPayload } from './order-approved';
import { orderCancelledPayload } from './order-cancelled';
import { orderCreatedPayload } from './order-created';
import { orderDeliveredPayload } from './order-delivered';
import { orderPickingStartedPayload } from './order-picking-started';
import { orderReadyForDeliveryRoutePayload } from './order-ready-for-delivery-route';
import { orderSettledPayload } from './order-settled';
import { paymentRegisteredPayload } from './payment-registered';
import { pickingCompletedPayload } from './picking-completed';
import { saleCreatedPayload } from './sale-created';
import { salesSessionClosedPayload } from './sales-session-closed';
import { salesSessionOpenedPayload } from './sales-session-opened';
import { userRegisteredPayload } from './user-registered';

export * from './delivery-confirmed';
export * from './delivery-failed';
export * from './delivery-route-assigned';
export * from './delivery-route-created';
export * from './delivery-route-dispatched';
export * from './inventory-released';
export * from './inventory-reservation-failed';
export * from './inventory-reserved';
export * from './order-approved';
export * from './order-cancelled';
export * from './order-created';
export * from './order-delivered';
export * from './order-picking-started';
export * from './delivery-route-liquidated';
export * from './order-settled';
export * from './order-ready-for-delivery-route';
export * from './picking-completed';
export * from './payment-registered';
export * from './sales-session-closed';
export * from './sales-session-opened';
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
  [DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE]: orderReadyForDeliveryRoutePayload,
  [DomainEventName.ORDER_DELIVERED]: orderDeliveredPayload,
  [DomainEventName.ORDER_SETTLED]: orderSettledPayload,
  [DomainEventName.DELIVERY_ROUTE_CREATED]: deliveryRouteCreatedPayload,
  [DomainEventName.DELIVERY_ROUTE_ASSIGNED]: deliveryRouteAssignedPayload,
  [DomainEventName.DELIVERY_ROUTE_DISPATCHED]: deliveryRouteDispatchedPayload,
  [DomainEventName.DELIVERY_CONFIRMED]: deliveryConfirmedPayload,
  [DomainEventName.DELIVERY_FAILED]: deliveryFailedPayload,
  [DomainEventName.DELIVERY_ROUTE_LIQUIDATED]: deliveryRouteLiquidatedPayload,
  [DomainEventName.SALES_SESSION_OPENED]: salesSessionOpenedPayload,
  [DomainEventName.SALES_SESSION_CLOSED]: salesSessionClosedPayload,
  [DomainEventName.SALE_CREATED]: saleCreatedPayload,
  [DomainEventName.PAYMENT_REGISTERED]: paymentRegisteredPayload,
} as const;

export type EventPayloadFor<TName extends DomainEventName> = z.infer<
  (typeof EventPayloadSchemas)[TName]
>;
