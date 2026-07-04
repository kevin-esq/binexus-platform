import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { ApproveOrderHandler } from './application/commands/approve-order.command';
import { CancelOrderHandler } from './application/commands/cancel-order.command';
import { CreateOrderHandler } from './application/commands/create-order.command';
import { MarkOrderDeliveredHandler } from './application/commands/mark-order-delivered.command';
import { MarkOrderDeliveryAttemptFailedHandler } from './application/commands/mark-order-delivery-attempt-failed.command';
import { MarkOrderOutForDeliveryHandler } from './application/commands/mark-order-out-for-delivery.command';
import { MarkOrderReadyForDeliveryRouteHandler } from './application/commands/mark-order-ready-for-delivery-route.command';
import { MoveOrderToPickingHandler } from './application/commands/move-order-to-picking.command';
import { RequeueFailedDeliveryOrderHandler } from './application/commands/requeue-failed-delivery-order.command';
import { OrdersReadService } from './application/orders-read.service';
import { DeliveryConfirmedOrdersHandler } from './events/delivery-confirmed.handler';
import { DeliveryFailedOrdersHandler } from './events/delivery-failed.handler';
import { DeliveryRouteDispatchedOrdersHandler } from './events/delivery-route-dispatched.handler';
import { InventoryReservationFailedOrdersHandler } from './events/inventory-reservation-failed.handler';
import { InventoryReservedOrdersHandler } from './events/inventory-reserved.handler';
import { PickingCompletedOrdersHandler } from './events/picking-completed.handler';
import { OrdersController } from './presentation/orders.controller';

const commandHandlers = [
  CreateOrderHandler,
  ApproveOrderHandler,
  CancelOrderHandler,
  MoveOrderToPickingHandler,
  MarkOrderReadyForDeliveryRouteHandler,
  MarkOrderOutForDeliveryHandler,
  MarkOrderDeliveredHandler,
  MarkOrderDeliveryAttemptFailedHandler,
  RequeueFailedDeliveryOrderHandler,
];

@Module({
  imports: [CommandsModule, EventsModule, PrismaModule, TenantModule],
  controllers: [OrdersController],
  providers: [
    ...commandHandlers,
    OrdersReadService,
    InventoryReservationFailedOrdersHandler,
    InventoryReservedOrdersHandler,
    PickingCompletedOrdersHandler,
    DeliveryRouteDispatchedOrdersHandler,
    DeliveryConfirmedOrdersHandler,
    DeliveryFailedOrdersHandler,
  ],
})
export class OrdersModule {}
