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
  RequeueFailedDeliveryOrderCommand,
  RequeueFailedDeliveryOrderHandler,
} from './requeue-failed-delivery-order.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('RequeueFailedDeliveryOrderHandler', () => {
  it('transitions DELIVERY_ATTEMPT_FAILED to READY_FOR_DELIVERY_ROUTE and emits ORDER_READY_FOR_DELIVERY_ROUTE', async () => {
    const order = {
      id: 'order-1',
      branchId: 'branch-1',
      state: OrderState.DELIVERY_ATTEMPT_FAILED,
      _count: { lines: 2 },
    };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const event = {
      id: 'evt-requeue-1',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      payload: {},
    };
    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new RequeueFailedDeliveryOrderHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new RequeueFailedDeliveryOrderCommand(
        'order-1' as OrderId,
        'user-1' as UserId,
        'Customer will be home tomorrow',
        { correlationId: 'corr-1' },
      ),
    );

    expect(result).toEqual({
      id: 'order-1',
      state: SharedOrderState.READY_FOR_DELIVERY_ROUTE,
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        fromState: OrderState.DELIVERY_ATTEMPT_FAILED,
        toState: OrderState.READY_FOR_DELIVERY_ROUTE,
        reason: 'Requeued after failed delivery — Customer will be home tomorrow',
      }),
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      expect.objectContaining({ orderId: 'order-1', branchId: 'branch-1', lineCount: 2 }),
      expect.objectContaining({ correlationId: 'corr-1' }),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });

  it('is idempotent when already READY_FOR_DELIVERY_ROUTE', async () => {
    const order = {
      id: 'order-1',
      branchId: 'branch-1',
      state: OrderState.READY_FOR_DELIVERY_ROUTE,
      _count: { lines: 1 },
    };
    const tx = {
      order: { findFirst: vi.fn().mockResolvedValue(order), update: vi.fn() },
      orderTransition: { create: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const outbox = { record: vi.fn() };

    const handler = new RequeueFailedDeliveryOrderHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new RequeueFailedDeliveryOrderCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result.state).toBe(SharedOrderState.READY_FOR_DELIVERY_ROUTE);
    expect(tx.order.update).not.toHaveBeenCalled();
    expect(outbox.record).not.toHaveBeenCalled();
  });

  it('rejects illegal transition from DELIVERED', async () => {
    const order = {
      id: 'order-1',
      branchId: 'branch-1',
      state: OrderState.DELIVERED,
      _count: { lines: 1 },
    };
    const tx = { order: { findFirst: vi.fn().mockResolvedValue(order) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new RequeueFailedDeliveryOrderHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(
        new RequeueFailedDeliveryOrderCommand('order-1' as OrderId, 'user-1' as UserId),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
