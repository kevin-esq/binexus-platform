import { PaymentMethod, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import {
  PaymentMethod as PrismaPaymentMethod,
  SalesSessionStatus,
  TicketStatus,
} from '@prisma/client';
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

const saleLine = {
  productId: 'product-demo-1',
  productName: 'Demo',
  quantity: 1,
  unitPriceCents: 10000,
};

function makeOpenSession() {
  return {
    id: 'session-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    terminalId: 'Caja 1',
    status: SalesSessionStatus.OPEN,
    currency: 'MXN',
  };
}

function makeHandler(
  tx: Record<string, unknown>,
  tenantContext: TenantContextService,
): { handler: CreateSaleHandler; outbox: { record: ReturnType<typeof vi.fn> } } {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;
  const eventBus = { build: vi.fn().mockReturnValue({ id: 'evt-1' }) };
  const outbox = { record: vi.fn().mockResolvedValue(undefined) };

  return {
    handler: new CreateSaleHandler(
      prisma,
      tenantContext,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    ),
    outbox,
  };
}

function makeSuccessfulSaleTx(
  paymentCreates: Array<{ id: string; method: PrismaPaymentMethod; amountCents: number }>,
) {
  let paymentCreateIndex = 0;
  return {
    salesSession: {
      findFirst: vi.fn().mockResolvedValue(makeOpenSession()),
    },
    stockItem: {
      findFirst: vi.fn().mockResolvedValue({ onHand: 10, reserved: 0 }),
      update: vi.fn().mockResolvedValue({ onHand: 9 }),
    },
    ticket: {
      create: vi.fn().mockResolvedValue({ id: 'ticket-1' }),
      findFirstOrThrow: vi.fn().mockImplementation(() =>
        Promise.resolve({
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
          paymentCaptures: paymentCreates.map((payment) => ({
            id: payment.id,
            method: payment.method,
            amountCents: payment.amountCents,
            currency: 'MXN',
            capturedAt: new Date('2026-07-10T12:00:00.000Z'),
          })),
        }),
      ),
    },
    ticketLine: { createMany: vi.fn().mockResolvedValue({ count: 1 }) },
    paymentCapture: {
      create: vi.fn().mockImplementation(() => {
        const payment = paymentCreates[paymentCreateIndex];
        paymentCreateIndex += 1;
        return Promise.resolve({
          id: payment.id,
          method: payment.method,
          amountCents: payment.amountCents,
          currency: 'MXN',
        });
      }),
    },
    stockMovement: { create: vi.fn().mockResolvedValue({ id: 'mov-1' }) },
  };
}

describe('CreateSaleHandler', () => {
  it('creates a single CASH sale (5.1 regression)', async () => {
    const tx = makeSuccessfulSaleTx([
      { id: 'pay-1', method: PrismaPaymentMethod.CASH, amountCents: 10000 },
    ]);

    const { handler, outbox } = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new CreateSaleCommand(
        'session-1',
        {
          lines: [saleLine],
          payments: [{ method: PaymentMethod.CASH, amountCents: 10000 }],
        },
        'cashier-1' as UserId,
        {},
      ),
    );

    expect(result.ticket.id).toBe('ticket-1');
    expect(result.ticket.paymentCaptures).toHaveLength(1);
    expect(result.ticket.paymentCaptures[0]?.method).toBe('CASH');
    expect(tx.paymentCapture.create).toHaveBeenCalledTimes(1);
    expect(outbox.record).toHaveBeenCalledTimes(2);
    expect(tx.stockMovement.create).toHaveBeenCalled();
  });

  it('creates a split sale with two payment methods', async () => {
    const tx = makeSuccessfulSaleTx([
      { id: 'pay-cash', method: PrismaPaymentMethod.CASH, amountCents: 5000 },
      { id: 'pay-card', method: PrismaPaymentMethod.CARD, amountCents: 5000 },
    ]);

    const { handler, outbox } = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    const result = await handler.execute(
      new CreateSaleCommand(
        'session-1',
        {
          lines: [saleLine],
          payments: [
            { method: PaymentMethod.CASH, amountCents: 5000 },
            { method: PaymentMethod.CARD, amountCents: 5000 },
          ],
        },
        'cashier-1' as UserId,
        {},
      ),
    );

    expect(result.ticket.paymentCaptures).toHaveLength(2);
    expect(tx.paymentCapture.create).toHaveBeenCalledTimes(2);
    expect(outbox.record).toHaveBeenCalledTimes(3);
  });

  it('creates a split sale with three or more payment lines', async () => {
    const tx = makeSuccessfulSaleTx([
      { id: 'pay-1', method: PrismaPaymentMethod.CASH, amountCents: 4000 },
      { id: 'pay-2', method: PrismaPaymentMethod.CARD, amountCents: 3000 },
      { id: 'pay-3', method: PrismaPaymentMethod.TRANSFER, amountCents: 3000 },
    ]);

    const { handler } = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    await handler.execute(
      new CreateSaleCommand(
        'session-1',
        {
          lines: [saleLine],
          payments: [
            { method: PaymentMethod.CASH, amountCents: 4000 },
            { method: PaymentMethod.CARD, amountCents: 3000 },
            { method: PaymentMethod.TRANSFER, amountCents: 3000 },
          ],
        },
        'cashier-1' as UserId,
        {},
      ),
    );

    expect(tx.paymentCapture.create).toHaveBeenCalledTimes(3);
  });

  it('rejects when payment sum does not match ticket total', async () => {
    const tx = {
      salesSession: { findFirst: vi.fn().mockResolvedValue(makeOpenSession()) },
    };

    const { handler } = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new CreateSaleCommand(
          'session-1',
          {
            lines: [saleLine],
            payments: [
              { method: PaymentMethod.CASH, amountCents: 4000 },
              { method: PaymentMethod.CARD, amountCents: 5000 },
            ],
          },
          'cashier-1' as UserId,
          {},
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('rejects sale when stock is insufficient', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue(makeOpenSession()),
      },
      stockItem: {
        findFirst: vi.fn().mockResolvedValue({ onHand: 0, reserved: 0 }),
      },
    };

    const { handler } = makeHandler(tx, {
      current: vi.fn().mockReturnValue(context),
    } as unknown as TenantContextService);

    await expect(
      handler.execute(
        new CreateSaleCommand(
          'session-1',
          {
            lines: [saleLine],
            payments: [{ method: PaymentMethod.CASH, amountCents: 10000 }],
          },
          'cashier-1' as UserId,
          {},
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
