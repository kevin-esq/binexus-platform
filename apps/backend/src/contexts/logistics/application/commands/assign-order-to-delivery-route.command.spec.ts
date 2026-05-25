import { DomainEventName } from '@binexus/events';
import type { OrderId, UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { DeliveryRouteCandidateStatus, DeliveryRouteStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  AssignOrderToDeliveryRouteCommand,
  AssignOrderToDeliveryRouteHandler,
} from './assign-order-to-delivery-route.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('AssignOrderToDeliveryRouteHandler', () => {
  it('assigns READY candidates to PLANNED route and emits DELIVERY_ROUTE_ASSIGNED', async () => {
    const route = {
      id: 'route-1',
      branchId: 'branch-1',
      status: DeliveryRouteStatus.PLANNED,
      _count: { stops: 0 },
    };

    const candidates = [
      {
        orderId: 'order-1',
        branchId: 'branch-1',
        status: DeliveryRouteCandidateStatus.READY,
      },
      {
        orderId: 'order-2',
        branchId: 'branch-1',
        status: DeliveryRouteCandidateStatus.READY,
      },
    ];

    const tx = {
      deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) },
      deliveryRouteCandidate: {
        findMany: vi.fn().mockResolvedValue(candidates),
        updateMany: vi.fn(),
      },
      deliveryRouteStop: { create: vi.fn() },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;

    const event = {
      id: 'evt-assign-1',
      name: DomainEventName.DELIVERY_ROUTE_ASSIGNED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: {
        deliveryRouteId: 'route-1',
        branchId: 'branch-1',
        orderIds: ['order-1', 'order-2'],
        assignedBy: 'user-1',
      },
    };

    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new AssignOrderToDeliveryRouteHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new AssignOrderToDeliveryRouteCommand(
        'route-1',
        ['order-1', 'order-2'] as OrderId[],
        'user-1' as UserId,
      ),
    );

    expect(result.assignedOrderIds).toEqual(['order-1', 'order-2']);
    expect(result.stopCount).toBe(2);
    expect(tx.deliveryRouteStop.create).toHaveBeenCalledTimes(2);
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });

  it('rejects mixed-branch candidates', async () => {
    const route = {
      id: 'route-1',
      branchId: 'branch-1',
      status: DeliveryRouteStatus.PLANNED,
      _count: { stops: 0 },
    };

    const candidates = [
      {
        orderId: 'order-1',
        branchId: 'branch-2',
        status: DeliveryRouteCandidateStatus.READY,
      },
    ];

    const tx = {
      deliveryRoute: { findFirst: vi.fn().mockResolvedValue(route) },
      deliveryRouteCandidate: { findMany: vi.fn().mockResolvedValue(candidates) },
      deliveryRouteStop: { create: vi.fn() },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;

    const handler = new AssignOrderToDeliveryRouteHandler(
      prisma,
      tenant,
      { build: vi.fn() } as unknown as EventBusService,
      { record: vi.fn() } as unknown as OutboxService,
    );

    await expect(
      handler.execute(
        new AssignOrderToDeliveryRouteCommand(
          'route-1',
          ['order-1'] as OrderId[],
          'user-1' as UserId,
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
