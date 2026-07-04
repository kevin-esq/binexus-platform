import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { LogisticsCandidateService } from '../application/logistics-candidate.service';

@Injectable()
export class OrderCancelledLogisticsHandler {
  constructor(
    @Inject(LogisticsCandidateService)
    private readonly logisticsCandidate: LogisticsCandidateService,
  ) {}

  @OnEvent(DomainEventName.ORDER_CANCELLED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>): Promise<void> {
    await this.logisticsCandidate.handleOrderCancelled(event);
  }
}
