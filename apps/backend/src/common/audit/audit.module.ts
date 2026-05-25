import { Module } from '@nestjs/common';

import { AuditLogService } from './audit-log.service';
import { OrderApprovedAuditHandler } from './order-approved-audit.handler';
import { OrderCreatedAuditHandler } from './order-created-audit.handler';

@Module({
  providers: [AuditLogService, OrderCreatedAuditHandler, OrderApprovedAuditHandler],
  exports: [AuditLogService],
})
export class AuditModule {}
