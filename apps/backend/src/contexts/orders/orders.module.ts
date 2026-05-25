import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { ApproveOrderHandler } from './application/commands/approve-order.command';
import { CancelOrderHandler } from './application/commands/cancel-order.command';
import { CreateOrderHandler } from './application/commands/create-order.command';
import { OrdersReadService } from './application/orders-read.service';
import { OrdersController } from './presentation/orders.controller';

const commandHandlers = [CreateOrderHandler, ApproveOrderHandler, CancelOrderHandler];

@Module({
  imports: [CommandsModule, EventsModule, PrismaModule, TenantModule],
  controllers: [OrdersController],
  providers: [...commandHandlers, OrdersReadService],
})
export class OrdersModule {}
