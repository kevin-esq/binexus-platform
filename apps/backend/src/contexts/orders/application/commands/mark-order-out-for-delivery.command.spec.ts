import { OrderState as SharedOrderState, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  MarkOrderOutForDeliveryCommand,
  MarkOrderOutForDeliveryHandler,
} from './mark-order-out-for-delivery.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('MarkOrderOutForDeliveryHandler', () => {
  it('transitions READY_FOR_DELIVERY_ROUTE to OUT_FOR_DELIVERY', async () => {
    const order = { id: 'order-1', state: OrderState.READY_FOR_DELIVERY_ROUTE };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderOutForDeliveryHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new MarkOrderOutForDeliveryCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result).toEqual({
      id: 'order-1',
      state: SharedOrderState.OUT_FOR_DELIVERY,
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith(
      expect.objectContaining({
        data: expect.objectContaining({
          reason: 'Delivery route dispatched',
          toState: OrderState.OUT_FOR_DELIVERY,
        }),
      }),
    );
  });

  it('is idempotent when already OUT_FOR_DELIVERY', async () => {
    const order = { id: 'order-1', state: OrderState.OUT_FOR_DELIVERY };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderOutForDeliveryHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new MarkOrderOutForDeliveryCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result.state).toBe(SharedOrderState.OUT_FOR_DELIVERY);
    expect(tx.order.update).not.toHaveBeenCalled();
    expect(tx.orderTransition.create).not.toHaveBeenCalled();
  });

  it('rejects illegal transition from APPROVED', async () => {
    const order = { id: 'order-1', state: OrderState.APPROVED };
    const tx = { order: { findFirst: vi.fn().mockResolvedValue(order) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderOutForDeliveryHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(new MarkOrderOutForDeliveryCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
