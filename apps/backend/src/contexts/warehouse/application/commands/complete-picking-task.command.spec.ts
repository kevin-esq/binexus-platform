import { DomainEventName, type DomainEvent } from '@binexus/events';
import { type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { PickingTaskStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  CompletePickingTaskCommand,
  CompletePickingTaskHandler,
} from './complete-picking-task.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function createHandlerFixture(status: PickingTaskStatus = PickingTaskStatus.PENDING): {
  handler: CompletePickingTaskHandler;
  tx: {
    pickingTask: {
      findFirst: ReturnType<typeof vi.fn>;
      update: ReturnType<typeof vi.fn>;
    };
    pickingLine: { update: ReturnType<typeof vi.fn> };
  };
  eventBus: { build: ReturnType<typeof vi.fn> };
  outbox: { record: ReturnType<typeof vi.fn> };
} {
  const task = {
    id: 'task-1',
    tenantId: 'tenant-1',
    orderId: 'order-1',
    branchId: 'branch-1',
    status,
    createdFromEventId: 'event-pick-1',
    completedByUserId: null,
    createdAt: new Date(),
    updatedAt: new Date(),
    completedAt: null,
    lines: [{ id: 'pline-1', quantity: 2, productId: 'sku-1', orderLineId: 'line-1' }],
  };

  const tx = {
    pickingTask: {
      findFirst: vi.fn().mockResolvedValue(task),
      update: vi.fn().mockResolvedValue({
        ...task,
        status: PickingTaskStatus.COMPLETED,
        completedAt: new Date(),
      }),
    },
    pickingLine: { update: vi.fn() },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<unknown>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  const event: DomainEvent = {
    id: 'event-done-1',
    name: DomainEventName.PICKING_COMPLETED,
    tenantId: tenantContext.tenantId,
    occurredAt: '2026-05-25T00:00:00.000Z',
    version: 1,
    payload: { orderId: 'order-1', pickingTaskId: 'task-1', completedBy: 'user-1' },
  };

  const eventBus = { build: vi.fn().mockReturnValue(event) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return {
    handler: new CompletePickingTaskHandler(
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

describe('CompletePickingTaskHandler', () => {
  it('marks lines picked, completes task, and emits PICKING_COMPLETED', async () => {
    const { handler, tx, eventBus, outbox } = createHandlerFixture();

    const result = await handler.execute(
      new CompletePickingTaskCommand('task-1', 'user-1' as UserId, {
        commandId: 'cmd-1',
        correlationId: 'corr-1',
      }),
    );

    expect(tx.pickingLine.update).toHaveBeenCalledWith({
      where: { id: 'pline-1' },
      data: { pickedQuantity: 2 },
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.PICKING_COMPLETED,
      { orderId: 'order-1', pickingTaskId: 'task-1', completedBy: 'user-1' },
      expect.any(Object),
    );
    expect(outbox.record).toHaveBeenCalled();
    expect(result.pickingTask.status).toBe('COMPLETED');
  });

  it('rejects non-pending task', async () => {
    const { handler } = createHandlerFixture(PickingTaskStatus.COMPLETED);

    await expect(
      handler.execute(new CompletePickingTaskCommand('task-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
