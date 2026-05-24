import { type DomainEvent, DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../prisma/prisma.service';

import { AuditLogService } from './audit-log.service';

const orderCreatedEvent: DomainEvent<typeof DomainEventName.ORDER_CREATED> = {
  id: 'event-1',
  name: DomainEventName.ORDER_CREATED,
  tenantId: 'tenant-1',
  occurredAt: '2026-05-24T00:00:00.000Z',
  version: 1,
  payload: {
    orderId: 'order-1',
    customerId: 'customer-1',
    totalCents: 2500,
    currency: 'MXN',
    createdBy: 'user-1',
  },
};

describe('AuditLogService', () => {
  it('upserts an audit row for ORDER_CREATED', async () => {
    const upsert = vi.fn().mockResolvedValue({});
    const prisma = {
      auditLog: { upsert },
    } as unknown as PrismaService;

    const service = new AuditLogService(prisma);
    await service.recordOrderCreated(orderCreatedEvent);

    expect(upsert).toHaveBeenCalledWith({
      where: { eventId: 'event-1' },
      create: {
        tenantId: 'tenant-1',
        eventId: 'event-1',
        eventName: DomainEventName.ORDER_CREATED,
        actorUserId: 'user-1',
        entityType: 'Order',
        entityId: 'order-1',
        action: DomainEventName.ORDER_CREATED,
        payload: orderCreatedEvent.payload,
        occurredAt: new Date('2026-05-24T00:00:00.000Z'),
      },
      update: {},
    });
  });
});
