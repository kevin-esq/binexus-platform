import { DomainEventName } from '@binexus/events';
import { OrderState as SharedOrderState, type OrderId, type UserId } from '@binexus/types';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  MarkOrderReadyForDeliveryRouteCommand,
  MarkOrderReadyForDeliveryRouteHandler,
} from './mark-order-ready-for-delivery-route.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

describe('MarkOrderReadyForDeliveryRouteHandler', () => {
  it('transitions PICKING to READY_FOR_DELIVERY_ROUTE and records ORDER_READY_FOR_DELIVERY_ROUTE', async () => {
    const order = {
      id: 'order-1',
      branchId: 'branch-1',
      state: OrderState.PICKING,
      _count: { lines: 2 },
    };

    const tx = {
      order: {
        findFirst: vi.fn().mockResolvedValue(order),
        update: vi.fn(),
      },
      orderTransition: { create: vi.fn() },
    };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const tenant = {
      current: vi.fn().mockReturnValue(tenantContext),
    } as unknown as TenantContextService;

    const event = {
      id: 'evt-ready-1',
      name: DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      tenantId: tenantContext.tenantId,
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: {
        orderId: 'order-1',
        branchId: 'branch-1',
        readyBy: 'user-1',
        lineCount: 2,
      },
      correlationId: 'corr-1',
      causationId: 'cmd-1',
    };

    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const handler = new MarkOrderReadyForDeliveryRouteHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    );

    const result = await handler.execute(
      new MarkOrderReadyForDeliveryRouteCommand('order-1' as OrderId, 'user-1' as UserId, {
        correlationId: 'corr-1',
        causationId: 'cmd-1',
        commandId: 'cmd-1',
      }),
    );

    expect(result).toEqual({
      id: 'order-1',
      state: SharedOrderState.READY_FOR_DELIVERY_ROUTE,
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
      expect.objectContaining({
        orderId: 'order-1',
        branchId: 'branch-1',
        readyBy: 'user-1',
        lineCount: 2,
      }),
      expect.objectContaining({ correlationId: 'corr-1', causationId: 'cmd-1' }),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });
});
