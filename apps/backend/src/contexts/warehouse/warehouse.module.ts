import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { EventsModule } from '../../common/events/events.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { CompletePickingTaskHandler } from './application/commands/complete-picking-task.command';
import { WarehousePickingService } from './application/warehouse-picking.service';
import { WarehouseReadService } from './application/warehouse-read.service';
import { OrderPickingStartedWarehouseHandler } from './events/order-picking-started.handler';
import { WarehouseController } from './presentation/warehouse.controller';

@Module({
  imports: [CommandsModule, EventsModule, PrismaModule, TenantModule],
  controllers: [WarehouseController],
  providers: [
    WarehousePickingService,
    WarehouseReadService,
    CompletePickingTaskHandler,
    OrderPickingStartedWarehouseHandler,
  ],
})
export class WarehouseModule {}
