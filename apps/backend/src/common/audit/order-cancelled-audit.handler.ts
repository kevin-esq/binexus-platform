import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { AuditLogService } from './audit-log.service';

@Injectable()
export class OrderCancelledAuditHandler {
  constructor(@Inject(AuditLogService) private readonly auditLog: AuditLogService) {}

  @OnEvent(DomainEventName.ORDER_CANCELLED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED>): Promise<void> {
    await this.auditLog.recordOrderCancelled(event);
  }
}
