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

import {
  MoveOrderToPickingCommand,
  MoveOrderToPickingHandler,
} from './move-order-to-picking.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function createHandlerFixture(orderState: OrderState = OrderState.APPROVED): {
  handler: MoveOrderToPickingHandler;
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
      findFirst: vi.fn().mockResolvedValue({
        id: 'order-1',
        state: orderState,
        branchId: 'branch-1',
        lines: [{ id: 'line-1' }, { id: 'line-2' }],
      }),
      update: vi.fn(),
    },
    orderTransition: { create: vi.fn() },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<unknown>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  const event: DomainEvent = {
    id: 'event-pick-1',
    name: DomainEventName.ORDER_PICKING_STARTED,
    tenantId: tenantContext.tenantId,
    occurredAt: '2026-05-25T00:00:00.000Z',
    version: 1,
    payload: { orderId: 'order-1', branchId: 'branch-1', lineCount: 2 },
  };

  const eventBus = { build: vi.fn().mockReturnValue(event) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return {
    handler: new MoveOrderToPickingHandler(
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

describe('MoveOrderToPickingCommand', () => {
  it('validates orderId', async () => {
    await expect(
      validateAppCommand(new MoveOrderToPickingCommand('' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('MoveOrderToPickingHandler', () => {
  it('transitions APPROVED to PICKING and emits ORDER_PICKING_STARTED', async () => {
    const { handler, tx, eventBus, outbox } = createHandlerFixture();

    const result = await handler.execute(
      new MoveOrderToPickingCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result.state).toBe(SharedOrderState.PICKING);
    expect(tx.order.update).toHaveBeenCalledWith({
      where: { id: 'order-1' },
      data: { state: OrderState.PICKING },
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_PICKING_STARTED,
      { orderId: 'order-1', branchId: 'branch-1', lineCount: 2 },
      expect.any(Object),
    );
    expect(outbox.record).toHaveBeenCalled();
  });

  it('rejects invalid state transition', async () => {
    const { handler } = createHandlerFixture(OrderState.DRAFT);

    await expect(
      handler.execute(new MoveOrderToPickingCommand('order-1' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('is idempotent when already PICKING', async () => {
    const { handler, tx, outbox } = createHandlerFixture(OrderState.PICKING);

    const result = await handler.execute(
      new MoveOrderToPickingCommand('order-1' as OrderId, 'user-1' as UserId),
    );

    expect(result.state).toBe(SharedOrderState.PICKING);
    expect(tx.order.update).not.toHaveBeenCalled();
    expect(outbox.record).not.toHaveBeenCalled();
  });

  it('throws when order not found', async () => {
    const { handler, tx } = createHandlerFixture();
    tx.order.findFirst.mockResolvedValue(null);

    await expect(
      handler.execute(new MoveOrderToPickingCommand('missing' as OrderId, 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(NotFoundException);
  });
});
