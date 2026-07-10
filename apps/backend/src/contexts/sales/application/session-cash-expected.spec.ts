import { PaymentMethod } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { computeSessionCashExpected } from './session-cash-expected';

describe('computeSessionCashExpected', () => {
  it('sums opening float and CASH captures in session', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({ openingFloatCents: 10000, currency: 'MXN' }),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([
          { amountCents: 5000, currency: 'MXN' },
          { amountCents: 2500, currency: 'MXN' },
        ]),
      },
    };

    const result = await computeSessionCashExpected(tx, 'session-1', 'tenant-1');

    expect(result).toEqual({ expectedCents: 17500, currency: 'MXN' });
    expect(tx.paymentCapture.findMany).toHaveBeenCalledWith({
      where: { sessionId: 'session-1', tenantId: 'tenant-1', method: PaymentMethod.CASH },
      select: { amountCents: true, currency: true },
    });
  });

  it('throws when payment currency differs from session currency', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({ openingFloatCents: 0, currency: 'MXN' }),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([{ amountCents: 100, currency: 'USD' }]),
      },
    };

    await expect(computeSessionCashExpected(tx, 'session-1', 'tenant-1')).rejects.toThrow(
      'SESSION_CASH_CURRENCY_MISMATCH',
    );
  });

  it('counts only CASH portions when session has mixed-method tickets', async () => {
    const tx = {
      salesSession: {
        findFirst: vi.fn().mockResolvedValue({ openingFloatCents: 10000, currency: 'MXN' }),
      },
      paymentCapture: {
        findMany: vi.fn().mockResolvedValue([
          { amountCents: 10000, currency: 'MXN' },
          { amountCents: 5000, currency: 'MXN' },
        ]),
      },
    };

    const result = await computeSessionCashExpected(tx, 'session-1', 'tenant-1');

    expect(result).toEqual({ expectedCents: 25000, currency: 'MXN' });
    expect(tx.paymentCapture.findMany).toHaveBeenCalledWith({
      where: { sessionId: 'session-1', tenantId: 'tenant-1', method: PaymentMethod.CASH },
      select: { amountCents: true, currency: true },
    });
  });
});
