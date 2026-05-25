import { Module } from '@nestjs/common';

import { PrismaModule } from '../../common/prisma/prisma.module';
import { TenantModule } from '../../common/tenant/tenant.module';

import { InventoryReadService } from './application/inventory-read.service';
import { InventoryReservationService } from './application/inventory-reservation.service';
import { OrderApprovedInventoryHandler } from './events/order-approved-inventory.handler';
import { OrderCancelledInventoryHandler } from './events/order-cancelled-inventory.handler';
import { InventoryController } from './presentation/inventory.controller';

@Module({
  imports: [PrismaModule, TenantModule],
  controllers: [InventoryController],
  providers: [
    InventoryReservationService,
    InventoryReadService,
    OrderApprovedInventoryHandler,
    OrderCancelledInventoryHandler,
  ],
})
export class InventoryModule {}
