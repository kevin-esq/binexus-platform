import { DomainEventName } from '@binexus/events';
import { describe, expect, it, vi } from 'vitest';

import { type LogisticsCandidateService } from '../application/logistics-candidate.service';

import { OrderCancelledLogisticsHandler } from './order-cancelled.handler';

describe('OrderCancelledLogisticsHandler', () => {
  it('delegates ORDER_CANCELLED to LogisticsCandidateService', async () => {
    const logisticsCandidate = {
      handleOrderCancelled: vi.fn().mockResolvedValue(undefined),
    };

    const handler = new OrderCancelledLogisticsHandler(
      logisticsCandidate as unknown as LogisticsCandidateService,
    );

    const event = {
      id: 'evt-cancel-1',
      name: DomainEventName.ORDER_CANCELLED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T19:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', cancelledBy: 'user-1' },
    };

    await handler.handle(event);

    expect(logisticsCandidate.handleOrderCancelled).toHaveBeenCalledWith(event);
  });
});
