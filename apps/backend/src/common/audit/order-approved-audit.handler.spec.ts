import { type DomainEvent, DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type AuditLogService } from './audit-log.service';
import { OrderApprovedAuditHandler } from './order-approved-audit.handler';

const event: DomainEvent<typeof DomainEventName.ORDER_APPROVED> = {
  id: 'event-2',
  name: DomainEventName.ORDER_APPROVED,
  tenantId: 'tenant-1',
  occurredAt: '2026-05-24T00:00:00.000Z',
  version: 1,
  payload: { orderId: 'order-1', approvedBy: 'user-1' },
};

describe('OrderApprovedAuditHandler', () => {
  it('delegates ORDER_APPROVED to AuditLogService', async () => {
    const auditLog = {
      recordOrderApproved: vi.fn().mockResolvedValue(undefined),
    } as unknown as AuditLogService;

    const handler = new OrderApprovedAuditHandler(auditLog);
    await handler.handle(event);

    expect(auditLog.recordOrderApproved).toHaveBeenCalledWith(event);
  });
});
