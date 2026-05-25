import { DomainEventName } from '@binexus/events';
import { OrderState as SharedOrderState, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  MarkOrderDeliveredCommand,
  MarkOrderDeliveredHandler,
} from './mark-order-delivered.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('MarkOrderDeliveredHandler', () => {
  it('transitions OUT_FOR_DELIVERY to DELIVERED and emits ORDER_DELIVERED', async () => {
    const order = {
      id: 'order-1',
      branchId: 'branch-1',
      state: OrderState.OUT_FOR_DELIVERY,
    };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const event = {
      id: 'evt-delivered-1',
      name: DomainEventName.ORDER_DELIVERED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1' },
    };
    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new MarkOrderDeliveredHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new MarkOrderDeliveredCommand('order-1' as OrderId, 'user-1' as UserId, {
        correlationId: 'corr-1',
      }),
    );

    expect(result).toEqual({ id: 'order-1', state: SharedOrderState.DELIVERED });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_DELIVERED,
      expect.objectContaining({ orderId: 'order-1', branchId: 'branch-1' }),
      expect.objectContaining({ correlationId: 'corr-1' }),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });

  it('is idempotent when already DELIVERED', async () => {
    const order = { id: 'order-1', branchId: 'branch-1', state: OrderState.DELIVERED };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const outbox = { record: vi.fn() };

    const handler = new MarkOrderDeliveredHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new MarkOrderDeliveredCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result.state).toBe(SharedOrderState.DELIVERED);
    expect(outbox.record).not.toHaveBeenCalled();
  });

  it('rejects illegal transition from APPROVED', async () => {
    const order = { id: 'order-1', branchId: 'branch-1', state: OrderState.APPROVED };
    const tx = { order: { findFirst: vi.fn().mockResolvedValue(order) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new MarkOrderDeliveredHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new MarkOrderDeliveredCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
