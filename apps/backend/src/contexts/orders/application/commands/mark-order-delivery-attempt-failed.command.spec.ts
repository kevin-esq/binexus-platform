import { OrderState as SharedOrderState, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  MarkOrderDeliveryAttemptFailedCommand,
  MarkOrderDeliveryAttemptFailedHandler,
} from './mark-order-delivery-attempt-failed.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('MarkOrderDeliveryAttemptFailedHandler', () => {
  it('transitions OUT_FOR_DELIVERY to DELIVERY_ATTEMPT_FAILED', async () => {
    const order = { id: 'order-1', state: OrderState.OUT_FOR_DELIVERY };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderDeliveryAttemptFailedHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new MarkOrderDeliveryAttemptFailedCommand(
        'order-1' as OrderId,
        'user-1' as UserId,
        'NO_RECIPIENT',
        'Gate closed',
      ),
    );

    expect(result).toEqual({
      id: 'order-1',
      state: SharedOrderState.DELIVERY_ATTEMPT_FAILED,
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith(
      expect.objectContaining({
        data: expect.objectContaining({
          toState: OrderState.DELIVERY_ATTEMPT_FAILED,
          reason: 'Delivery failed: no recipient — Gate closed',
        }),
      }),
    );
  });

  it('is idempotent when already DELIVERY_ATTEMPT_FAILED', async () => {
    const order = { id: 'order-1', state: OrderState.DELIVERY_ATTEMPT_FAILED };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderDeliveryAttemptFailedHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new MarkOrderDeliveryAttemptFailedCommand('order-1' as OrderId, 'user-1' as UserId, 'OTHER'),
    );

    expect(result.state).toBe(SharedOrderState.DELIVERY_ATTEMPT_FAILED);
    expect(tx.order.update).not.toHaveBeenCalled();
  });

  it('rejects illegal transition from DELIVERED', async () => {
    const order = { id: 'order-1', state: OrderState.DELIVERED };
    const tx = { order: { findFirst: vi.fn().mockResolvedValue(order) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderDeliveryAttemptFailedHandler(prisma, {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new MarkOrderDeliveryAttemptFailedCommand(
          'order-1' as OrderId,
          'user-1' as UserId,
          'REFUSED',
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
