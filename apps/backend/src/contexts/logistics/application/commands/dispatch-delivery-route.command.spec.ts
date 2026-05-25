import { DomainEventName } from '@binexus/events';
import type { OrderId, UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { DeliveryRouteStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  DispatchDeliveryRouteCommand,
  DispatchDeliveryRouteHandler,
} from './dispatch-delivery-route.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function makeRoute(overrides: Record<string, unknown> = {}) {
  return {
    id: 'route-1',
    branchId: 'branch-1',
    status: DeliveryRouteStatus.PLANNED,
    driverUserId: 'driver-1',
    dispatchedAt: null,
    stops: [
      { orderId: 'order-1', sequence: 1 },
      { orderId: 'order-2', sequence: 2 },
    ],
    ...overrides,
  };
}

describe('DispatchDeliveryRouteHandler', () => {
  it('dispatches PLANNED route with stops and emits DELIVERY_ROUTE_DISPATCHED', async () => {
    const route = makeRoute();
    const tx = {
      deliveryRoute: {
        findFirst: vi.fn().mockResolvedValue(route),
        update: vi.fn().mockResolvedValue({
          ...route,
          status: DeliveryRouteStatus.DISPATCHED,
          dispatchedAt: new Date('2026-05-25T16:00:00.000Z'),
        }),
      },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;

    const event = {
      id: 'evt-dispatch-1',
      name: DomainEventName.DELIVERY_ROUTE_DISPATCHED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T16:00:00.000Z',
      version: 1,
      payload: {},
    };

    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId, undefined, {
        correlationId: 'corr-1',
        commandId: 'cmd-1',
      }),
    );

    expect(result.status).toBe('DISPATCHED');
    expect(result.driverUserId).toBe('driver-1');
    expect(result.orderIds).toEqual(['order-1', 'order-2'] as OrderId[]);
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.DELIVERY_ROUTE_DISPATCHED,
      expect.objectContaining({
        deliveryRouteId: 'route-1',
        branchId: 'branch-1',
        driverUserId: 'driver-1',
        orderIds: ['order-1', 'order-2'],
        dispatchedBy: 'user-1',
      }),
      expect.objectContaining({ correlationId: 'corr-1', causationId: 'cmd-1' }),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });

  it('uses command driverUserId when route has no driver', async () => {
    const route = makeRoute({ driverUserId: null });
    const tx = {
      deliveryRoute: {
        findFirst: vi.fn().mockResolvedValue(route),
        update: vi.fn().mockResolvedValue(route),
      },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;
    const eventBus = {
      build: vi
        .fn()
        .mockReturnValue({ id: 'evt-1', name: DomainEventName.DELIVERY_ROUTE_DISPATCHED }),
    };
    const outbox = { record: vi.fn() };

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    await handler.execute(
      new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId, 'driver-2' as UserId),
    );

    expect(tx.deliveryRoute.update).toHaveBeenCalledWith(
      expect.objectContaining({
        data: expect.objectContaining({ driverUserId: 'driver-2' }),
      }),
    );
  });

  it('rejects empty route', async () => {
    const route = makeRoute({ stops: [] });
    const tx = { deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects non-planned route', async () => {
    const route = makeRoute({ status: DeliveryRouteStatus.COMPLETED });
    const tx = { deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects missing driver on planned route', async () => {
    const route = makeRoute({ driverUserId: null });
    const tx = { deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('is idempotent when already DISPATCHED without re-emitting', async () => {
    const route = makeRoute({
      status: DeliveryRouteStatus.DISPATCHED,
      dispatchedAt: new Date('2026-05-25T12:00:00.000Z'),
      stops: [{ orderId: 'order-1', sequence: 1 }],
    });
    const tx = { deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const outbox = { record: vi.fn() };

    const handler = new DispatchDeliveryRouteHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new DispatchDeliveryRouteCommand('route-1', 'user-1' as UserId),
    );

    expect(result.dispatchedAt).toBe('2026-05-25T12:00:00.000Z');
    expect(outbox.record).not.toHaveBeenCalled();
  });
});
