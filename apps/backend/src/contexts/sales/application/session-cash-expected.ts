import { PaymentMethod } from '@prisma/client';

type SessionCashTx = {
  salesSession: {
    findFirst: (args: {
      where: { id: string; tenantId: string };
      select: { openingFloatCents: true; currency: true };
    }) => Promise<{ openingFloatCents: number; currency: string } | null>;
  };
  paymentCapture: {
    findMany: (args: {
      where: { sessionId: string; tenantId: string; method: PaymentMethod };
      select: { amountCents: true; currency: true };
    }) => Promise<Array<{ amountCents: number; currency: string }>>;
  };
};

export async function computeSessionCashExpected(
  tx: SessionCashTx,
  sessionId: string,
  tenantId: string,
): Promise<{ expectedCents: number; currency: string }> {
  const session = await tx.salesSession.findFirst({
    where: { id: sessionId, tenantId },
    select: { openingFloatCents: true, currency: true },
  });

  if (!session) {
    throw new Error('SESSION_NOT_FOUND');
  }

  const payments = await tx.paymentCapture.findMany({
    where: { sessionId, tenantId, method: PaymentMethod.CASH },
    select: { amountCents: true, currency: true },
  });

  for (const payment of payments) {
    if (payment.currency !== session.currency) {
      throw new Error('SESSION_CASH_CURRENCY_MISMATCH');
    }
  }

  const cashSalesCents = payments.reduce((sum, payment) => sum + payment.amountCents, 0);

  return {
    expectedCents: session.openingFloatCents + cashSalesCents,
    currency: session.currency,
  };
}
