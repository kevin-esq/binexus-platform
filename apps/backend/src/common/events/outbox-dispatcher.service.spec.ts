import { DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../prisma/prisma.service';

import { OutboxDispatcherService } from './outbox-dispatcher.service';
import { type EventTransport } from './transports/event-transport.token';

const baseRow = {
  id: 'event-1',
  tenantId: 'tenant-1',
  name: DomainEventName.ORDER_CREATED,
  payload: {
    orderId: 'order-1',
    customerId: 'customer-1',
    totalCents: 1000,
    currency: 'MXN',
    createdBy: 'user-1',
  },
  version: 1,
  occurredAt: new Date('2026-05-24T00:00:00.000Z'),
  publishedAt: null,
  attempts: 0,
  lastError: null,
  correlationId: 'corr-1',
  causationId: 'cause-1',
};

function createDispatcherFixture() {
  const publish = vi.fn().mockResolvedValue(undefined);
  const transport = { publish } as unknown as EventTransport;

  const findMany = vi.fn().mockResolvedValue([baseRow]);
  const update = vi.fn().mockResolvedValue({});
  const prisma = {
    outboxEvent: { findMany, update },
  } as unknown as PrismaService;

  const dispatcher = new OutboxDispatcherService(prisma, transport);

  return { dispatcher, publish, findMany, update };
}

describe('OutboxDispatcherService', () => {
  it('publishes pending events and marks publishedAt', async () => {
    const { dispatcher, publish, findMany, update } = createDispatcherFixture();

    const result = await dispatcher.dispatchPending();

    expect(result).toEqual({ published: 1, failed: 0 });
    expect(findMany).toHaveBeenCalledWith({
      where: { publishedAt: null },
      orderBy: { occurredAt: 'asc' },
      take: 50,
    });
    expect(publish).toHaveBeenCalledOnce();
    expect(publish.mock.calls[0]?.[0]).toMatchObject({
      id: 'event-1',
      name: DomainEventName.ORDER_CREATED,
      tenantId: 'tenant-1',
    });
    expect(update).toHaveBeenCalledWith({
      where: { id: 'event-1' },
      data: { publishedAt: expect.any(Date) },
    });
  });

  it('increments attempts and stores lastError when transport fails', async () => {
    const { dispatcher, publish, update } = createDispatcherFixture();
    publish.mockRejectedValueOnce(new Error('transport down'));

    const result = await dispatcher.dispatchPending();

    expect(result).toEqual({ published: 0, failed: 1 });
    expect(update).toHaveBeenCalledWith({
      where: { id: 'event-1' },
      data: {
        attempts: { increment: 1 },
        lastError: 'transport down',
      },
    });
  });

  it('records failure for unknown event names without publishing', async () => {
    const { dispatcher, publish, findMany, update } = createDispatcherFixture();
    findMany.mockResolvedValue([{ ...baseRow, id: 'event-bad', name: 'NOT_A_REAL_EVENT' }]);

    const result = await dispatcher.dispatchPending();

    expect(result).toEqual({ published: 0, failed: 1 });
    expect(publish).not.toHaveBeenCalled();
    expect(update).toHaveBeenCalledWith(
      expect.objectContaining({
        where: { id: 'event-bad' },
        data: expect.objectContaining({
          attempts: { increment: 1 },
          lastError: expect.stringContaining('Unknown or unsupported'),
        }),
      }),
    );
  });
});
