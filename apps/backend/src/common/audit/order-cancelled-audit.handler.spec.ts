import { type DomainEvent, DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type AuditLogService } from './audit-log.service';
import { OrderCancelledAuditHandler } from './order-cancelled-audit.handler';

const event: DomainEvent<typeof DomainEventName.ORDER_CANCELLED> = {
  id: 'event-3',
  name: DomainEventName.ORDER_CANCELLED,
  tenantId: 'tenant-1',
  occurredAt: '2026-05-24T00:00:00.000Z',
  version: 1,
  payload: { orderId: 'order-1', cancelledBy: 'user-1', reason: 'Customer request' },
};

describe('OrderCancelledAuditHandler', () => {
  it('delegates ORDER_CANCELLED to AuditLogService', async () => {
    const auditLog = {
      recordOrderCancelled: vi.fn().mockResolvedValue(undefined),
    } as unknown as AuditLogService;

    const handler = new OrderCancelledAuditHandler(auditLog);
    await handler.handle(event);

    expect(auditLog.recordOrderCancelled).toHaveBeenCalledWith(event);
  });
});
