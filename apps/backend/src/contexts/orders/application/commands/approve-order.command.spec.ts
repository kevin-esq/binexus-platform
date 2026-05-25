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

import { ApproveOrderCommand, ApproveOrderHandler } from './approve-order.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function createHandlerFixture(orderState: OrderState = OrderState.DRAFT): {
  handler: ApproveOrderHandler;
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
        ) => Promise<{ id: OrderId; state: typeof SharedOrderState.APPROVED }>,
      ) => callback(tx),
    ),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  const event: DomainEvent = {
    id: 'event-2',
    name: DomainEventName.ORDER_APPROVED,
    tenantId: tenantContext.tenantId,
    occurredAt: '2026-05-24T00:00:00.000Z',
    version: 1,
    payload: { orderId: 'order-1', approvedBy: 'user-1' },
  };

  const eventBus = { build: vi.fn().mockReturnValue(event) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return {
    handler: new ApproveOrderHandler(
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

describe('ApproveOrderCommand', () => {
  it('validates required orderId', async () => {
    const command = new ApproveOrderCommand('' as OrderId, 'user-1' as UserId, {
      commandId: 'cmd-1',
    });

    await expect(validateAppCommand(command)).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('ApproveOrderHandler', () => {
  it('approves a DRAFT order, records transition, and writes ORDER_APPROVED to outbox', async () => {
    const { handler, tx, eventBus, outbox } = createHandlerFixture();
    const command = new ApproveOrderCommand('order-1' as OrderId, 'user-1' as UserId, {
      commandId: 'cmd-approve-1',
      correlationId: 'corr-1',
    });

    const result = await handler.execute(command);

    expect(result).toEqual({ id: 'order-1', state: SharedOrderState.APPROVED });
    expect(tx.order.findFirst).toHaveBeenCalledWith({
      where: { id: 'order-1', tenantId: 'tenant-1' },
      select: { id: true, state: true },
    });
    expect(tx.order.update).toHaveBeenCalledWith({
      where: { id: 'order-1' },
      data: { state: OrderState.APPROVED },
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith({
      data: {
        tenantId: 'tenant-1',
        orderId: 'order-1',
        fromState: OrderState.DRAFT,
        toState: OrderState.APPROVED,
        reason: 'Order approved',
        byUserId: 'user-1',
      },
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_APPROVED,
      { orderId: 'order-1', approvedBy: 'user-1' },
      { correlationId: 'corr-1', causationId: 'cmd-approve-1' },
    );
    expect(outbox.record).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'event-2', name: DomainEventName.ORDER_APPROVED }),
      tx,
    );
  });

  it('rejects when order is not found', async () => {
    const { handler, tx } = createHandlerFixture();
    tx.order.findFirst.mockResolvedValue(null);

    await expect(
      handler.execute(new ApproveOrderCommand('missing' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(NotFoundException);
  });

  it('rejects invalid state transitions', async () => {
    const { handler } = createHandlerFixture(OrderState.APPROVED);

    await expect(
      handler.execute(new ApproveOrderCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
