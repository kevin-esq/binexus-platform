import { DomainEventName } from '@binexus/events';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type AppCommandBus } from '../../../common/commands/command-bus.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type SystemUserService } from '../../../common/tenant/system-user.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MarkOrderOutForDeliveryCommand } from '../application/commands/mark-order-out-for-delivery.command';

import { DeliveryRouteDispatchedOrdersHandler } from './delivery-route-dispatched.handler';

describe('DeliveryRouteDispatchedOrdersHandler', () => {
  it('marks READY_FOR_DELIVERY_ROUTE orders out for delivery', async () => {
    const commandBus = { execute: vi.fn().mockResolvedValue({}) };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const findFirst = vi
      .fn()
      .mockResolvedValueOnce({ id: 'order-1', state: OrderState.READY_FOR_DELIVERY_ROUTE })
      .mockResolvedValueOnce({ id: 'order-2', state: OrderState.READY_FOR_DELIVERY_ROUTE });
    const prisma = {
      forTenant: vi.fn().mockReturnValue({ order: { findFirst } }),
    };

    const handler = new DeliveryRouteDispatchedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-dispatch-1',
      name: DomainEventName.DELIVERY_ROUTE_DISPATCHED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T16:00:00.000Z',
      version: 1,
      correlationId: 'corr-1',
      payload: {
        deliveryRouteId: 'route-1',
        branchId: 'branch-1',
        driverUserId: 'driver-1',
        orderIds: ['order-1', 'order-2'],
        dispatchedBy: 'user-1',
        dispatchedAt: '2026-05-25T16:00:00.000Z',
      },
    });

    expect(commandBus.execute).toHaveBeenCalledTimes(2);
    expect(commandBus.execute).toHaveBeenCalledWith(expect.any(MarkOrderOutForDeliveryCommand));
  });

  it('skips orders already OUT_FOR_DELIVERY or not ready', async () => {
    const commandBus = { execute: vi.fn() };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const findFirst = vi
      .fn()
      .mockResolvedValueOnce({ id: 'order-1', state: OrderState.OUT_FOR_DELIVERY })
      .mockResolvedValueOnce({ id: 'order-2', state: OrderState.CANCELLED });
    const prisma = {
      forTenant: vi.fn().mockReturnValue({ order: { findFirst } }),
    };

    const handler = new DeliveryRouteDispatchedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-dispatch-1',
      name: DomainEventName.DELIVERY_ROUTE_DISPATCHED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T16:00:00.000Z',
      version: 1,
      payload: {
        deliveryRouteId: 'route-1',
        branchId: 'branch-1',
        driverUserId: 'driver-1',
        orderIds: ['order-1', 'order-2'],
        dispatchedBy: 'user-1',
        dispatchedAt: '2026-05-25T16:00:00.000Z',
      },
    });

    expect(commandBus.execute).not.toHaveBeenCalled();
  });
});
