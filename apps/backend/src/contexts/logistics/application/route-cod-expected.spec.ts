import { DeliveryRouteStopStatus, PaymentMethod } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { computeRouteCodExpected } from './route-cod-expected';

describe('computeRouteCodExpected', () => {
  it('sums only DELIVERED CASH stops and excludes FAILED and CARD', async () => {
    const tx = {
      deliveryRouteStop: {
        findMany: vi.fn().mockResolvedValue([
          { id: 'stop-1', orderId: 'order-cash' },
          { id: 'stop-3', orderId: 'order-card' },
        ]),
      },
      order: {
        findMany: vi.fn().mockResolvedValue([
          {
            id: 'order-cash',
            totalCents: 5000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CASH,
          },
          {
            id: 'order-failed',
            totalCents: 2000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CASH,
          },
          {
            id: 'order-card',
            totalCents: 3000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CARD,
          },
        ]),
      },
    };

    const result = await computeRouteCodExpected(tx as never, 'route-1', 'tenant-1');

    expect(tx.deliveryRouteStop.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: expect.objectContaining({
          status: DeliveryRouteStopStatus.DELIVERED,
        }),
      }),
    );
    expect(result.expectedCents).toBe(5000);
    expect(result.cashOrderIds).toEqual(['order-cash']);
    expect(result.stops).toHaveLength(1);
  });
});
