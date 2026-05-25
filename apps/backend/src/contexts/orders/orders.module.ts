import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { ApproveOrderHandler } from './application/commands/approve-order.command';
import { CancelOrderHandler } from './application/commands/cancel-order.command';
import { CreateOrderHandler } from './application/commands/create-order.command';
import { MarkOrderReadyForDeliveryRouteHandler } from './application/commands/mark-order-ready-for-delivery-route.command';
import { MoveOrderToPickingHandler } from './application/commands/move-order-to-picking.command';
import { OrdersReadService } from './application/orders-read.service';
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
  ],
})
export class OrdersModule {}
