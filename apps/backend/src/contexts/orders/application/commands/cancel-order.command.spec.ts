import { DomainEventName, type DomainEvent } from '@binexus/events';
import { OrderState as SharedOrderState, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException, NotFoundException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { validateAppCommand } from '../../../../common/commands/command-validation';
import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { CancelOrderCommand, CancelOrderHandler } from './cancel-order.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function createHandlerFixture(orderState: OrderState = OrderState.DRAFT): {
  handler: CancelOrderHandler;
  tx: {
    order: {
      findFirst: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
    };
    orderTransition: { create: ReturnType<typeof vi.fn> };
  };
  eventBus: { build: ReturnType<typeof vi.fn> };
  outbox: { record: ReturnType<typeof vi.fn> };
} {
  const tx = {
    order: {
      findFirst: vi.fn().mockResolvedValue({ id: 'order-1', state: orderState }),
      update: vi.fn().mockResolvedValue({ id: 'order-1' }),
    },
    orderTransition: { create: vi.fn().mockResolvedValue({ id: 'transition-2' }) },
  };

  const prisma = {
    $transaction: vi.fn(
      (
        callback: (
          client: typeof tx,
        ) => Promise<{ id: OrderId; state: typeof SharedOrderState.CANCELLED }>,
      ) => callback(tx),
    ),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  const event: DomainEvent = {
    id: 'event-3',
    name: DomainEventName.ORDER_CANCELLED,
    tenantId: tenantContext.tenantId,
    occurredAt: '2026-05-24T00:00:00.000Z',
    version: 1,
    payload: { orderId: 'order-1', cancelledBy: 'user-1', reason: 'Customer request' },
  };

  const eventBus = { build: vi.fn().mockReturnValue(event) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return {
    handler: new CancelOrderHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    ),
    tx,
    eventBus,
    outbox,
  };
}

describe('CancelOrderCommand', () => {
  it('validates required orderId', async () => {
    const command = new CancelOrderCommand('' as OrderId, 'user-1' as UserId, undefined, {
      commandId: 'cmd-1',
    });

    await expect(validateAppCommand(command)).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('CancelOrderHandler', () => {
  it('cancels a DRAFT order, records transition, and writes ORDER_CANCELLED to outbox', async () => {
    const { handler, tx, eventBus, outbox } = createHandlerFixture();
    const command = new CancelOrderCommand(
      'order-1' as OrderId,
      'user-1' as UserId,
      'Customer request',
      {
        commandId: 'cmd-cancel-1',
        correlationId: 'corr-1',
      },
    );

    const result = await handler.execute(command);

    expect(result).toEqual({ id: 'order-1', state: SharedOrderState.CANCELLED });
    expect(tx.order.findFirst).toHaveBeenCalledWith({
      where: { id: 'order-1', tenantId: 'tenant-1' },
      select: { id: true, state: true },
    });
    expect(tx.order.update).toHaveBeenCalledWith({
      where: { id: 'order-1' },
      data: { state: OrderState.CANCELLED },
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith({
      data: {
        tenantId: 'tenant-1',
        orderId: 'order-1',
        fromState: OrderState.DRAFT,
        toState: OrderState.CANCELLED,
        reason: 'Customer request',
        byUserId: 'user-1',
      },
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_CANCELLED,
      { orderId: 'order-1', cancelledBy: 'user-1', reason: 'Customer request' },
      { correlationId: 'corr-1', causationId: 'cmd-cancel-1' },
    );
    expect(outbox.record).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'event-3', name: DomainEventName.ORDER_CANCELLED }),
      tx,
    );
  });

  it('cancels an APPROVED order', async () => {
    const { handler } = createHandlerFixture(OrderState.APPROVED);

    await expect(
      handler.execute(new CancelOrderCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).resolves.toEqual({ id: 'order-1', state: SharedOrderState.CANCELLED });
  });

  it('rejects when order is not found', async () => {
    const { handler, tx } = createHandlerFixture();
    tx.order.findFirst.mockResolvedValue(null);

    await expect(
      handler.execute(new CancelOrderCommand('missing' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(NotFoundException);
  });

  it('rejects invalid state transitions', async () => {
    const { handler } = createHandlerFixture(OrderState.PICKING);

    await expect(
      handler.execute(new CancelOrderCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
