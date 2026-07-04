import { DomainEventName } from '@binexus/events';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type AppCommandBus } from '../../../common/commands/command-bus.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type SystemUserService } from '../../../common/tenant/system-user.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderDeliveryAttemptFailedCommand } from '../application/commands/mark-order-delivery-attempt-failed.command';

import { DeliveryFailedOrdersHandler } from './delivery-failed.handler';

describe('DeliveryFailedOrdersHandler', () => {
  it('marks OUT_FOR_DELIVERY order as delivery attempt failed', async () => {
    const commandBus = { execute: vi.fn().mockResolvedValue({}) };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi
            .fn()
            .mockResolvedValue({ id: 'order-1', state: OrderState.OUT_FOR_DELIVERY }),
        },
      }),
    };

    const handler = new DeliveryFailedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-failed-1',
      name: DomainEventName.DELIVERY_FAILED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      correlationId: 'corr-1',
      payload: {
        deliveryRouteId: 'route-1',
        deliveryRouteStopId: 'stop-1',
        branchId: 'branch-1',
        orderId: 'order-1',
        failureReason: 'NO_RECIPIENT',
        failureNotes: 'Nobody home',
        reportedBy: 'user-1',
        reportedAt: '2026-05-25T17:00:00.000Z',
      },
    });

    expect(commandBus.execute).toHaveBeenCalledWith(
      expect.any(MarkOrderDeliveryAttemptFailedCommand),
    );
  });

  it('skips orders not OUT_FOR_DELIVERY', async () => {
    const commandBus = { execute: vi.fn() };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi
            .fn()
            .mockResolvedValue({ id: 'order-1', state: OrderState.DELIVERY_ATTEMPT_FAILED }),
        },
      }),
    };

    const handler = new DeliveryFailedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-failed-1',
      name: DomainEventName.DELIVERY_FAILED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      payload: {
        deliveryRouteId: 'route-1',
        deliveryRouteStopId: 'stop-1',
        branchId: 'branch-1',
        orderId: 'order-1',
        failureReason: 'REFUSED',
        reportedBy: 'user-1',
        reportedAt: '2026-05-25T17:00:00.000Z',
      },
    });

    expect(commandBus.execute).not.toHaveBeenCalled();
  });
});
