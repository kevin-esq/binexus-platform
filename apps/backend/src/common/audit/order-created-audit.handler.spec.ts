import { type DomainEvent, DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type AuditLogService } from './audit-log.service';
import { OrderCreatedAuditHandler } from './order-created-audit.handler';

const event: DomainEvent<typeof DomainEventName.ORDER_CREATED> = {
  id: 'event-1',
  name: DomainEventName.ORDER_CREATED,
  tenantId: 'tenant-1',
  occurredAt: '2026-05-24T00:00:00.000Z',
  version: 1,
  payload: {
    orderId: 'order-1',
    customerId: 'customer-1',
    totalCents: 1000,
    currency: 'MXN',
    createdBy: 'user-1',
  },
};

describe('OrderCreatedAuditHandler', () => {
  it('delegates ORDER_CREATED to AuditLogService', async () => {
    const recordOrderCreated = vi.fn().mockResolvedValue(undefined);
    const auditLog = { recordOrderCreated } as unknown as AuditLogService;
    const handler = new OrderCreatedAuditHandler(auditLog);

    await handler.handle(event);

    expect(recordOrderCreated).toHaveBeenCalledWith(event);
  });
});
