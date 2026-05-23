import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { CreateOrderHandler } from './application/commands/create-order.command';
import { OrdersController } from './presentation/orders.controller';

const commandHandlers = [CreateOrderHandler];

@Module({
  imports: [CommandsModule, EventsModule, PrismaModule, TenantModule],
  controllers: [OrdersController],
  providers: [...commandHandlers],
})
export class OrdersModule {}
