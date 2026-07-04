import { type UserId } from '@binexus/types';
import { ConflictException, ForbiddenException } from '@nestjs/common';
import { DeliveryRouteStatus, DeliveryRouteStopStatus, PaymentMethod, Role } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  LiquidateDeliveryRouteCommand,
  LiquidateDeliveryRouteHandler,
} from './liquidate-delivery-route.command';

const adminContext = {
  tenantId: 'tenant-1',
  userId: 'admin-1',
  role: Role.ADMIN,
  branchId: 'branch-1',
  requestId: 'request-1',
};

const driverContext = {
  ...adminContext,
  userId: 'driver-1',
  role: Role.DRIVER,
};

function makeRoute(overrides: Record<string, unknown> = {}) {
  return {
    id: 'route-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    status: DeliveryRouteStatus.COMPLETED,
    liquidation: null,
    ...overrides,
  };
}

function makeHandler(
  tx: Record<string, unknown>,
  tenantContext: TenantContextService,
): LiquidateDeliveryRouteHandler {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;
  const eventBus = { build: vi.fn().mockReturnValue({ id: 'evt-1' }) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return new LiquidateDeliveryRouteHandler(
    prisma,
    tenantContext,
    eventBus as unknown as EventBusService,
    outbox as unknown as OutboxService,
  );
}

describe('LiquidateDeliveryRouteHandler', () => {
  it('liquidates a COMPLETED route when declared matches COD expected (no lines)', async () => {
    const tx = {
      deliveryRoute: {
        findFirst: vi.fn().mockResolvedValue(makeRoute()),
      },
      deliveryRouteStop: {
        findMany: vi.fn().mockResolvedValue([
          { id: 'stop-cash', orderId: 'order-cash' },
          { id: 'stop-card', orderId: 'order-card' },
        ]),
      },
      order: {
        findMany: vi.fn().mockResolvedValue([
          {
            id: 'order-cash',
            totalCents: 5000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CASH,
          },
          {
            id: 'order-failed',
            totalCents: 2000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CASH,
          },
          {
            id: 'order-card',
            totalCents: 3000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CARD,
          },
        ]),
      },
      deliveryRouteLiquidation: {
        create: vi.fn().mockImplementation(({ data, include }) =>
          Promise.resolve({
            id: 'liq-1',
            deliveryRouteId: 'route-1',
            expectedCents: data.expectedCents,
            declaredCents: data.declaredCents,
            discrepancyCents: data.discrepancyCents,
            currency: data.currency,
            closedAt: new Date('2026-07-04T12:00:00.000Z'),
            discrepancyReason: data.discrepancyReason,
            lines: include?.lines ? [] : [],
          }),
        ),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(adminContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new LiquidateDeliveryRouteCommand(
        'route-1',
        { declaredCents: 5000 },
        'admin-1' as UserId,
        {},
      ),
    );

    expect(result.liquidation.expectedCents).toBe(5000);
    expect(result.liquidation.declaredCents).toBe(5000);
    expect(result.liquidation.discrepancyCents).toBe(0);
    expect(tx.deliveryRouteLiquidation.create).toHaveBeenCalledWith(
      expect.objectContaining({
        data: expect.objectContaining({
          expectedCents: 5000,
          declaredCents: 5000,
          discrepancyCents: 0,
        }),
      }),
    );
  });

  it('rejects discrepancy without supervisor role', async () => {
    const tx = {
      deliveryRoute: { findFirst: vi.fn().mockResolvedValue(makeRoute()) },
      deliveryRouteStop: {
        findMany: vi
          .fn()
          .mockResolvedValue([
            { id: 'stop-cash', orderId: 'order-cash', status: DeliveryRouteStopStatus.DELIVERED },
          ]),
      },
      order: {
        findMany: vi.fn().mockResolvedValue([
          {
            id: 'order-cash',
            totalCents: 5000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.CASH,
          },
        ]),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(driverContext),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new LiquidateDeliveryRouteCommand(
          'route-1',
          {
            declaredCents: 4800,
            discrepancyReason: 'Driver short',
            lines: [{ deliveryRouteStopId: 'stop-cash', declaredCents: 4800 }],
          },
          'driver-1' as UserId,
          {},
        ),
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('rejects double liquidation with 409', async () => {
    const tx = {
      deliveryRoute: {
        findFirst: vi.fn().mockResolvedValue(makeRoute({ liquidation: { id: 'liq-existing' } })),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(adminContext),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new LiquidateDeliveryRouteCommand('route-1', { declaredCents: 0 }, 'admin-1' as UserId, {}),
      ),
    ).rejects.toBeInstanceOf(ConflictException);
  });

  it('allows zero expected when route has no COD stops', async () => {
    const tx = {
      deliveryRoute: { findFirst: vi.fn().mockResolvedValue(makeRoute()) },
      deliveryRouteStop: {
        findMany: vi
          .fn()
          .mockResolvedValue([
            { id: 'stop-card', orderId: 'order-card', status: DeliveryRouteStopStatus.DELIVERED },
          ]),
      },
      order: {
        findMany: vi.fn().mockResolvedValue([
          {
            id: 'order-card',
            totalCents: 3000,
            currency: 'MXN',
            paymentMethod: PaymentMethod.TRANSFER,
          },
        ]),
      },
      deliveryRouteLiquidation: {
        create: vi.fn().mockResolvedValue({
          id: 'liq-1',
          deliveryRouteId: 'route-1',
          expectedCents: 0,
          declaredCents: 0,
          discrepancyCents: 0,
          currency: 'MXN',
          closedAt: new Date('2026-07-04T12:00:00.000Z'),
          discrepancyReason: null,
          lines: [],
        }),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(adminContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new LiquidateDeliveryRouteCommand('route-1', { declaredCents: 0 }, 'admin-1' as UserId, {}),
    );

    expect(result.liquidation.expectedCents).toBe(0);
  });
});
