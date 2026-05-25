import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { WarehousePickingService } from '../application/warehouse-picking.service';

@Injectable()
export class OrderPickingStartedWarehouseHandler {
  constructor(
    @Inject(WarehousePickingService)
    private readonly warehousePicking: WarehousePickingService,
  ) {}

  @OnEvent(DomainEventName.ORDER_PICKING_STARTED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_PICKING_STARTED>): Promise<void> {
    await this.warehousePicking.handleOrderPickingStarted(event);
  }
}
