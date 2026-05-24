import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { AuditLogService } from './audit-log.service';

@Injectable()
export class OrderCreatedAuditHandler {
  constructor(@Inject(AuditLogService) private readonly auditLog: AuditLogService) {}

  @OnEvent(DomainEventName.ORDER_CREATED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_CREATED>): Promise<void> {
    await this.auditLog.recordOrderCreated(event);
  }
}
