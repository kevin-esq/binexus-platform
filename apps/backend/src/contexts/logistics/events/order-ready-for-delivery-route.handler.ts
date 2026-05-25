import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { LogisticsCandidateService } from '../application/logistics-candidate.service';

@Injectable()
export class OrderReadyForDeliveryRouteLogisticsHandler {
  constructor(
    @Inject(LogisticsCandidateService)
    private readonly logisticsCandidate: LogisticsCandidateService,
  ) {}

  @OnEvent(DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE)
  async handle(
    event: DomainEvent<typeof DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE>,
  ): Promise<void> {
    await this.logisticsCandidate.handleOrderReadyForDeliveryRoute(event);
  }
}
