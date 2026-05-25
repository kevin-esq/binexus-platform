import { Module } from '@nestjs/common';

import { InventoryReservationService } from './application/inventory-reservation.service';
import { OrderApprovedInventoryHandler } from './events/order-approved-inventory.handler';
import { OrderCancelledInventoryHandler } from './events/order-cancelled-inventory.handler';

@Module({
  providers: [
    InventoryReservationService,
    OrderApprovedInventoryHandler,
    OrderCancelledInventoryHandler,
  ],
})
export class InventoryModule {}
