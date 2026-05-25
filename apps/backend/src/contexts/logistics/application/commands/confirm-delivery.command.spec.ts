import { DomainEventName } from '@binexus/events';
import type { UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { DeliveryRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { ConfirmDeliveryCommand, ConfirmDeliveryHandler } from './confirm-delivery.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function makeStop(overrides: Record<string, unknown> = {}) {
  return {
    id: 'stop-1',
    tenantId: 'tenant-1',
    deliveryRouteId: 'route-1',
    orderId: 'order-1',
    sequence: 1,
    status: DeliveryRouteStopStatus.PLANNED,
    deliveredAt: null,
    deliveryRoute: {
      id: 'route-1',
      branchId: 'branch-1',
      status: DeliveryRouteStatus.DISPATCHED,
      completedAt: null,
    },
    ...overrides,
  };
}

describe('ConfirmDeliveryHandler', () => {
  it('confirms PLANNED stop on DISPATCHED route and emits DELIVERY_CONFIRMED', async () => {
    const stop = makeStop();
    const tx = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
        update: vi.fn(),
        count: vi.fn().mockResolvedValue(0),
      },
      deliveryRoute: {
        update: vi.fn().mockResolvedValue({
          id: 'route-1',
          status: DeliveryRouteStatus.COMPLETED,
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
      id: 'evt-confirm-1',
      name: DomainEventName.DELIVERY_CONFIRMED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      payload: {},
    };
    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new ConfirmDeliveryHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new ConfirmDeliveryCommand('stop-1', 'user-1' as UserId, { correlationId: 'corr-1' }),
    );

    expect(result.status).toBe('DELIVERED');
    expect(result.orderId).toBe('order-1');
    expect(result.routeStatus).toBe('COMPLETED');
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.DELIVERY_CONFIRMED,
      expect.objectContaining({
        deliveryRouteStopId: 'stop-1',
        orderId: 'order-1',
        branchId: 'branch-1',
      }),
      expect.objectContaining({ correlationId: 'corr-1' }),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
    expect(tx.deliveryRoute.update).toHaveBeenCalled();
  });

  it('does not complete route when other stops remain PLANNED', async () => {
    const stop = makeStop();
    const tx = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
        update: vi.fn(),
        count: vi.fn().mockResolvedValue(1),
      },
      deliveryRoute: { update: vi.fn() },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      {
        build: vi.fn().mockReturnValue({ id: 'evt-1', name: DomainEventName.DELIVERY_CONFIRMED }),
      } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    const result = await handler.execute(new ConfirmDeliveryCommand('stop-1', 'user-1' as UserId));

    expect(result.routeStatus).toBe('DISPATCHED');
    expect(tx.deliveryRoute.update).not.toHaveBeenCalled();
  });

  it('is idempotent when stop already DELIVERED without re-emitting', async () => {
    const stop = makeStop({
      status: DeliveryRouteStopStatus.DELIVERED,
      deliveredAt: new Date('2026-05-25T16:00:00.000Z'),
      deliveryRoute: {
        id: 'route-1',
        branchId: 'branch-1',
        status: DeliveryRouteStatus.COMPLETED,
      },
    });
    const tx = { deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const outbox = { record: vi.fn() };

    const handler = new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(new ConfirmDeliveryCommand('stop-1', 'user-1' as UserId));

    expect(result.deliveredAt).toBe('2026-05-25T16:00:00.000Z');
    expect(outbox.record).not.toHaveBeenCalled();
  });

  it('rejects non-planned stop', async () => {
    const stop = makeStop({ status: DeliveryRouteStopStatus.FAILED });
    const tx = { deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new ConfirmDeliveryCommand('stop-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects when route is not DISPATCHED', async () => {
    const stop = makeStop({
      deliveryRoute: {
        id: 'route-1',
        branchId: 'branch-1',
        status: DeliveryRouteStatus.PLANNED,
      },
    });
    const tx = { deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new ConfirmDeliveryCommand('stop-1', 'user-1' as UserId)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
