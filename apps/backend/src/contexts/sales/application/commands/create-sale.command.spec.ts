import { type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { PaymentMethod, SalesSessionStatus, TicketStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { CreateSaleCommand, CreateSaleHandler } from './create-sale.command';

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
): CreateSaleHandler {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;
  const eventBus = { build: vi.fn().mockReturnValue({ id: 'evt-1' }) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return new CreateSaleHandler(
    prisma,
    tenantContext,
    eventBus as unknown as EventBusService,
    outbox as unknown as OutboxService,
  );
}

describe('CreateSaleHandler', () => {
  it('creates a cash sale and decrements stock for an OPEN session', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({
          id: 'session-1',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          terminalId: 'Caja 1',
          status: SalesSessionStatus.OPEN,
          currency: 'MXN',
        }),
      },
      stockItem: {
        findFirst: vi.fn().mockResolvedValue({ onHand: 10, reserved: 0 }),
        update: vi.fn().mockResolvedValue({ onHand: 9 }),
      },
      ticket: {
        create: vi.fn().mockResolvedValue({ id: 'ticket-1' }),
        findFirstOrThrow: vi.fn().mockResolvedValue({
          id: 'ticket-1',
          sessionId: 'session-1',
          branchId: 'branch-1',
          terminalId: 'Caja 1',
          customerLabel: 'walk-in',
          status: TicketStatus.COMPLETED,
          totalCents: 10000,
          currency: 'MXN',
          cashierUserId: 'cashier-1',
          createdAt: new Date('2026-07-10T12:00:00.000Z'),
          lines: [
            {
              productId: 'product-demo-1',
              productName: 'Demo',
              quantity: 1,
              unitPriceCents: 10000,
              lineTotalCents: 10000,
            },
          ],
        }),
      },
      ticketLine: { createMany: vi.fn().mockResolvedValue({ count: 1 }) },
      paymentCapture: {
        create: vi.fn().mockResolvedValue({ id: 'pay-1' }),
      },
      stockMovement: { create: vi.fn().mockResolvedValue({ id: 'mov-1' }) },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new CreateSaleCommand(
        'session-1',
        {
          lines: [
            {
              productId: 'product-demo-1',
              productName: 'Demo',
              quantity: 1,
              unitPriceCents: 10000,
            },
          ],
        },
        'cashier-1' as UserId,
        {},
      ),
    );

    expect(result.ticket.id).toBe('ticket-1');
    expect(tx.paymentCapture.create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        method: PaymentMethod.CASH,
        amountCents: 10000,
      }),
    });
    expect(tx.stockMovement.create).toHaveBeenCalled();
  });

  it('rejects sale when stock is insufficient', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({
          id: 'session-1',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          terminalId: 'Caja 1',
          status: SalesSessionStatus.OPEN,
          currency: 'MXN',
        }),
      },
      stockItem: {
        findFirst: vi.fn().mockResolvedValue({ onHand: 0, reserved: 0 }),
      },
    };

    const handler = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new CreateSaleCommand(
          'session-1',
          {
            lines: [
              {
                productId: 'product-demo-1',
                productName: 'Demo',
                quantity: 1,
                unitPriceCents: 100,
              },
            ],
          },
          'cashier-1' as UserId,
          {},
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
