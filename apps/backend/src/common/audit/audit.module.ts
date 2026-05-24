import { Module } from '@nestjs/common';

import { AuditLogService } from './audit-log.service';
import { OrderCreatedAuditHandler } from './order-created-audit.handler';

@Module({
  providers: [AuditLogService, OrderCreatedAuditHandler],
  exports: [AuditLogService],
})
export class AuditModule {}
