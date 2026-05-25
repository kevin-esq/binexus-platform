import { type DomainEvent, DomainEventName } from '@binexus/events';
import { Inject, Injectable } from '@nestjs/common';
import { OnEvent } from '@nestjs/event-emitter';

import { AuditLogService } from './audit-log.service';

@Injectable()
export class OrderApprovedAuditHandler {
  constructor(@Inject(AuditLogService) private readonly auditLog: AuditLogService) {}

  @OnEvent(DomainEventName.ORDER_APPROVED)
  async handle(event: DomainEvent<typeof DomainEventName.ORDER_APPROVED>): Promise<void> {
    await this.auditLog.recordOrderApproved(event);
  }
}
