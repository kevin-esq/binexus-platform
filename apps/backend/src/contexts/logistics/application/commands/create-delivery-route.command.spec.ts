import { DomainEventName } from '@binexus/events';
import type { BranchId, UserId } from '@binexus/types';
import { DeliveryRouteStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  CreateDeliveryRouteCommand,
  CreateDeliveryRouteHandler,
} from './create-delivery-route.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('CreateDeliveryRouteHandler', () => {
  it('creates PLANNED route and emits DELIVERY_ROUTE_CREATED', async () => {
    const route = {
      id: 'route-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      status: DeliveryRouteStatus.PLANNED,
      driverUserId: null,
      plannedDate: null,
      createdByUserId: 'user-1',
      createdAt: new Date(),
      updatedAt: new Date(),
    };

    const tx = { deliveryRoute: { create: vi.fn().mockResolvedValue(route) } };
    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;
    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;

    const event = {
      id: 'evt-route-1',
      name: DomainEventName.DELIVERY_ROUTE_CREATED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: {
        deliveryRouteId: 'route-1',
        branchId: 'branch-1',
        createdBy: 'user-1',
      },
    };

    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new CreateDeliveryRouteHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new CreateDeliveryRouteCommand(
        'branch-1' as BranchId,
        'user-1' as UserId,
        undefined,
        undefined,
        {
          correlationId: 'corr-1',
        },
      ),
    );

    expect(result.deliveryRoute.id).toBe('route-1');
    expect(result.deliveryRoute.status).toBe('PLANNED');
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });
});
