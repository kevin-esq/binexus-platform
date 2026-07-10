import { type UserId } from '@binexus/types';
import { ConflictException } from '@nestjs/common';
import { SalesSessionStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { OpenSalesSessionCommand, OpenSalesSessionHandler } from './open-sales-session.command';

const context = {
  tenantId: 'tenant-1',
  userId: 'cashier-1',
  role: 'CASHIER',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function makeHandler(
  tx: Record<string, unknown>,
  tenantContext: TenantContextService,
): OpenSalesSessionHandler {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;
  const eventBus = { build: vi.fn().mockReturnValue({ id: 'evt-1' }) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return new OpenSalesSessionHandler(
    prisma,
    tenantContext,
    eventBus as unknown as EventBusService,
    outbox as unknown as OutboxService,
  );
}

describe('OpenSalesSessionHandler', () => {
  it('opens a session for a terminal when none is OPEN', async () => {
    const createdAt = new Date('2026-07-10T12:00:00.000Z');
    const tx = {
      branch: { findFirst: vi.fn().mockResolvedValue({ id: 'branch-1' }) },
      salesSession: {
        findFirst: vi.fn().mockResolvedValue(null),
        create: vi.fn().mockResolvedValue({
          id: 'session-1',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          terminalId: 'Caja 1',
          status: SalesSessionStatus.OPEN,
          openingFloatCents: 50000,
          currency: 'MXN',
          openedByUserId: 'cashier-1',
          openedAt: createdAt,
          closedByUserId: null,
          closedAt: null,
          expectedClosingCents: null,
          declaredClosingCents: null,
          discrepancyCents: null,
          discrepancyReason: null,
          closeNotes: null,
        }),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new OpenSalesSessionCommand(undefined, 'Caja 1', 50000, 'MXN', 'cashier-1' as UserId, {}),
    );

    expect(result.session.id).toBe('session-1');
    expect(result.session.terminalId).toBe('Caja 1');
    expect(tx.salesSession.findFirst).toHaveBeenCalledWith({
      where: {
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        terminalId: 'Caja 1',
        status: SalesSessionStatus.OPEN,
      },
      select: { id: true },
    });
  });

  it('rejects a second OPEN session on the same terminal', async () => {
    const tx = {
      branch: { findFirst: vi.fn().mockResolvedValue({ id: 'branch-1' }) },
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({ id: 'session-existing' }),
        create: vi.fn(),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new OpenSalesSessionCommand(undefined, 'Caja 1', 0, 'MXN', 'cashier-1' as UserId, {}),
      ),
    ).rejects.toBeInstanceOf(ConflictException);
  });
});
