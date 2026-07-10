import { type UserId } from '@binexus/types';
import { ForbiddenException } from '@nestjs/common';
import { Role, SalesSessionStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { CloseSalesSessionCommand, CloseSalesSessionHandler } from './close-sales-session.command';

const adminContext = {
  tenantId: 'tenant-1',
  userId: 'admin-1',
  role: Role.ADMIN,
  branchId: 'branch-1',
  requestId: 'request-1',
};

const cashierContext = {
  ...adminContext,
  userId: 'cashier-1',
  role: Role.CASHIER,
};

function makeOpenSession(overrides: Record<string, unknown> = {}) {
  return {
    id: 'session-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    terminalId: 'Caja 1',
    status: SalesSessionStatus.OPEN,
    openingFloatCents: 10000,
    currency: 'MXN',
    openedByUserId: 'cashier-1',
    openedAt: new Date('2026-07-10T08:00:00.000Z'),
    closedByUserId: null,
    closedAt: null,
    expectedClosingCents: null,
    declaredClosingCents: null,
    discrepancyCents: null,
    discrepancyReason: null,
    closeNotes: null,
    ...overrides,
  };
}

function makeHandler(
  tx: Record<string, unknown>,
  tenantContext: TenantContextService,
): CloseSalesSessionHandler {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;
  const eventBus = { build: vi.fn().mockReturnValue({ id: 'evt-1' }) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return new CloseSalesSessionHandler(
    prisma,
    tenantContext,
    eventBus as unknown as EventBusService,
    outbox as unknown as OutboxService,
  );
}

describe('CloseSalesSessionHandler', () => {
  it('closes when declared matches expected', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue(makeOpenSession()),
        update: vi.fn().mockImplementation(({ data }) =>
          Promise.resolve({
            ...makeOpenSession(),
            status: SalesSessionStatus.CLOSED,
            ...data,
            closedAt: data.closedAt,
          }),
        ),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([{ amountCents: 5000, currency: 'MXN' }]),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(cashierContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new CloseSalesSessionCommand(
        'session-1',
        { declaredClosingCents: 15000 },
        'cashier-1' as UserId,
        {},
      ),
    );

    expect(result.session.status).toBe('CLOSED');
    expect(result.session.expectedClosingCents).toBe(15000);
    expect(result.session.declaredClosingCents).toBe(15000);
  });

  it('rejects cashier closing with discrepancy', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue(makeOpenSession()),
        update: vi.fn(),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([]),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(cashierContext),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new CloseSalesSessionCommand(
          'session-1',
          { declaredClosingCents: 9000, discrepancyReason: 'short' },
          'cashier-1' as UserId,
          {},
        ),
      ),
    ).rejects.toBeInstanceOf(ForbiddenException);
  });

  it('allows admin to close with discrepancy and reason', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue(makeOpenSession()),
        update: vi.fn().mockImplementation(({ data }) =>
          Promise.resolve({
            ...makeOpenSession(),
            status: SalesSessionStatus.CLOSED,
            ...data,
          }),
        ),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([]),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(adminContext),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new CloseSalesSessionCommand(
        'session-1',
        { declaredClosingCents: 9000, discrepancyReason: 'counting error' },
        'admin-1' as UserId,
        {},
      ),
    );

    expect(result.session.discrepancyCents).toBe(-1000);
    expect(result.session.discrepancyReason).toBe('counting error');
  });
});
