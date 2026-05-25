import { DomainEventName } from '@binexus/events';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type AppCommandBus } from '../../../common/commands/command-bus.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderReadyForDeliveryRouteCommand } from '../application/commands/mark-order-ready-for-delivery-route.command';

import { PickingCompletedOrdersHandler } from './picking-completed.handler';

describe('PickingCompletedOrdersHandler', () => {
  it('marks PICKING order ready for delivery route', async () => {
    const commandBus = { execute: vi.fn().mockResolvedValue({}) };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi.fn().mockResolvedValue({ id: 'order-1', state: OrderState.PICKING }),
        },
      }),
    };

    const handler = new PickingCompletedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-done-1',
      name: DomainEventName.PICKING_COMPLETED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', pickingTaskId: 'task-1', completedBy: 'user-1' },
    });

    expect(commandBus.execute).toHaveBeenCalledWith(
      expect.any(MarkOrderReadyForDeliveryRouteCommand),
    );
  });

  it('no-ops when order already READY_FOR_DELIVERY_ROUTE', async () => {
    const commandBus = { execute: vi.fn() };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi
            .fn()
            .mockResolvedValue({ id: 'order-1', state: OrderState.READY_FOR_DELIVERY_ROUTE }),
        },
      }),
    };

    const handler = new PickingCompletedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-done-1',
      name: DomainEventName.PICKING_COMPLETED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', pickingTaskId: 'task-1', completedBy: 'user-1' },
    });

    expect(commandBus.execute).not.toHaveBeenCalled();
  });
});
