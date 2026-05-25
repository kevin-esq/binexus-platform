import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { InventoryReservationService } from '../application/inventory-reservation.service';

@Injectable()
export class OrderCancelledInventoryHandler {
  constructor(
    @Inject(InventoryReservationService)
    private readonly inventory: InventoryReservationService,
  ) {}

  @OnEvent(DomainEventName.ORDER_CANCELLED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>): Promise<void> {
    await this.inventory.handleOrderCancelled(event);
  }
}
