import { Module } from '@nestjs/common';

import { CommandsModule } from '../../common/commands/commands.module';
import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { AdjustStockHandler } from './application/commands/adjust-stock.command';
import { InventoryReadService } from './application/inventory-read.service';
import { InventoryReservationService } from './application/inventory-reservation.service';
import { OrderApprovedInventoryHandler } from './events/order-approved-inventory.handler';
import { OrderCancelledInventoryHandler } from './events/order-cancelled-inventory.handler';
import { InventoryController } from './presentation/inventory.controller';

@Module({
  imports: [CommandsModule, PrismaModule, TenantModule],
  controllers: [InventoryController],
  providers: [
    InventoryReservationService,
    InventoryReadService,
    AdjustStockHandler,
    OrderApprovedInventoryHandler,
    OrderCancelledInventoryHandler,
  ],
})
export class InventoryModule {}
