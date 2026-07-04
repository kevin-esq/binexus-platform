import { DomainEventName } from '@binexus/events';
import type { UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import {
  DeliveryFailureReason,
  DeliveryRouteStatus,
  DeliveryRouteStopStatus,
} from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  ReportFailedDeliveryCommand,
  ReportFailedDeliveryHandler,
} from './report-failed-delivery.command';

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
    failedAt: null,
    failureReason: null,
    failureNotes: null,
    deliveryRoute: {
      id: 'route-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      status: DeliveryRouteStatus.DISPATCHED,
    },
    ...overrides,
  };
}

function makeHandler(
  prisma: PrismaService,
  tenant: TenantContextService,
  eventBus: EventBusService,
  outbox: OutboxService,
) {
  return new ReportFailedDeliveryHandler(prisma, tenant, eventBus, outbox);
}

function mockStopCounts() {
  return vi
    .fn()
    .mockResolvedValue([{ status: DeliveryRouteStopStatus.FAILED, _count: { _all: 1 } }]);
}

describe('ReportFailedDeliveryHandler', () => {
  it('marks PLANNED stop FAILED, emits DELIVERY_FAILED, and completes route', async () => {
    const stop = makeStop();
    const tx = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
        update: vi.fn(),
        count: vi.fn().mockResolvedValue(0),
        groupBy: mockStopCounts(),
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
    const event = {
      id: 'evt-failed-1',
      name: DomainEventName.DELIVERY_FAILED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T17:00:00.000Z',
      version: 1,
      payload: {},
    };
    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = makeHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new ReportFailedDeliveryCommand('stop-1', 'user-1' as UserId, 'NO_RECIPIENT', 'Nobody home', {
        correlationId: 'corr-1',
      }),
    );

    expect(result.status).toBe('FAILED');
    expect(result.failureReason).toBe('NO_RECIPIENT');
    expect(result.routeStatus).toBe('COMPLETED');
    expect(result.routeStopCounts.failed).toBe(1);
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.DELIVERY_FAILED,
      expect.objectContaining({
        deliveryRouteStopId: 'stop-1',
        orderId: 'order-1',
        failureReason: 'NO_RECIPIENT',
        failureNotes: 'Nobody home',
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
        groupBy: vi.fn().mockResolvedValue([
          { status: DeliveryRouteStopStatus.FAILED, _count: { _all: 1 } },
          { status: DeliveryRouteStopStatus.PLANNED, _count: { _all: 1 } },
        ]),
      },
      deliveryRoute: { update: vi.fn() },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = makeHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn().mockReturnValue({ id: 'evt-1' }) } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    const result = await handler.execute(
      new ReportFailedDeliveryCommand('stop-1', 'user-1' as UserId, 'REFUSED'),
    );

    expect(result.routeStatus).toBe('DISPATCHED');
    expect(tx.deliveryRoute.update).not.toHaveBeenCalled();
  });

  it('is idempotent when stop already FAILED without re-emitting', async () => {
    const stop = makeStop({
      status: DeliveryRouteStopStatus.FAILED,
      failedAt: new Date('2026-05-25T16:00:00.000Z'),
      failureReason: DeliveryFailureReason.WRONG_ADDRESS,
      deliveryRoute: {
        id: 'route-1',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        status: DeliveryRouteStatus.COMPLETED,
      },
    });
    const tx = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
        groupBy: mockStopCounts(),
      },
    };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const outbox = { record: vi.fn() };

    const handler = makeHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new ReportFailedDeliveryCommand('stop-1', 'user-1' as UserId, 'OTHER'),
    );

    expect(result.failedAt).toBe('2026-05-25T16:00:00.000Z');
    expect(result.failureReason).toBe('WRONG_ADDRESS');
    expect(outbox.record).not.toHaveBeenCalled();
  });

  it('rejects non-planned stop', async () => {
    const stop = makeStop({ status: DeliveryRouteStopStatus.DELIVERED });
    const tx = { deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = makeHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new ReportFailedDeliveryCommand('stop-1', 'user-1' as UserId, 'DAMAGED')),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects when route is not DISPATCHED', async () => {
    const stop = makeStop({
      deliveryRoute: {
        id: 'route-1',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        status: DeliveryRouteStatus.PLANNED,
      },
    });
    const tx = { deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = makeHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(new ReportFailedDeliveryCommand('stop-1', 'user-1' as UserId, 'OTHER')),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
