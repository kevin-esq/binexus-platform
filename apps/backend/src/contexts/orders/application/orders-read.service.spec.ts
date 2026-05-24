import { BadRequestException, NotFoundException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../common/prisma/prisma.service';

import { OrdersReadService } from './orders-read.service';

const createdAt = new Date('2026-05-24T10:00:00.000Z');

function createFixture(): {
  service: OrdersReadService;
  db: {
    order: {
      findMany: ReturnType<typeof vi.fn>;
      findFirst: ReturnType<typeof vi.fn>;
    };
  };
} {
  const db = {
    order: {
      findMany: vi.fn(),
      findFirst: vi.fn(),
    },
  };

  const prisma = {
    forTenant: vi.fn().mockReturnValue(db),
  } as unknown as PrismaService;

  return { service: new OrdersReadService(prisma), db };
}

describe('OrdersReadService', () => {
  it('lists orders with pagination cursor', async () => {
    const { service, db } = createFixture();
    db.order.findMany.mockResolvedValue([
      {
        id: 'order-2',
        branchId: 'branch-1',
        customerId: 'customer-1',
        state: OrderState.DRAFT,
        totalCents: 1000,
        currency: 'MXN',
        createdAt,
        _count: { lines: 1 },
      },
      {
        id: 'order-1',
        branchId: 'branch-1',
        customerId: 'customer-1',
        state: OrderState.DRAFT,
        totalCents: 2000,
        currency: 'MXN',
        createdAt: new Date('2026-05-24T09:00:00.000Z'),
        _count: { lines: 2 },
      },
    ]);

    const result = await service.listOrders({ limit: 1 });

    expect(result.items).toHaveLength(1);
    expect(result.items[0]?.id).toBe('order-2');
    expect(result.nextCursor).toBe('order-2');
    expect(db.order.findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        take: 2,
        orderBy: [{ createdAt: 'desc' }, { id: 'desc' }],
      }),
    );
  });

  it('rejects an invalid cursor', async () => {
    const { service, db } = createFixture();
    db.order.findFirst.mockResolvedValue(null);

    await expect(service.listOrders({ cursor: 'missing' })).rejects.toBeInstanceOf(
      BadRequestException,
    );
  });

  it('returns order detail with lines and transitions', async () => {
    const { service, db } = createFixture();
    db.order.findFirst.mockResolvedValue({
      id: 'order-1',
      branchId: 'branch-1',
      customerId: 'customer-1',
      state: OrderState.DRAFT,
      totalCents: 2500,
      currency: 'MXN',
      createdByUserId: 'user-1',
      createdAt,
      updatedAt: createdAt,
      lines: [
        {
          id: 'line-1',
          productId: 'product-1',
          productName: 'Coffee',
          quantity: 2,
          unitPriceCents: 1250,
          lineTotalCents: 2500,
        },
      ],
      transitions: [
        {
          id: 'transition-1',
          fromState: null,
          toState: OrderState.DRAFT,
          reason: 'Order created',
          occurredAt: createdAt,
          byUserId: 'user-1',
        },
      ],
      _count: { lines: 1 },
    });

    const detail = await service.getOrderById('order-1');

    expect(detail.id).toBe('order-1');
    expect(detail.lines).toHaveLength(1);
    expect(detail.transitions).toHaveLength(1);
    expect(detail.lineCount).toBe(1);
  });

  it('throws when order is not found', async () => {
    const { service, db } = createFixture();
    db.order.findFirst.mockResolvedValue(null);

    await expect(service.getOrderById('missing')).rejects.toBeInstanceOf(NotFoundException);
  });
});
