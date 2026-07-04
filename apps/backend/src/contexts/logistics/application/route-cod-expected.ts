import {
  PaymentMethod as PrismaPaymentMethod,
  DeliveryRouteStopStatus,
  type Order,
  type Prisma,
} from '@prisma/client';

export interface CodStopExpected {
  stopId: string;
  orderId: string;
  expectedCents: number;
  currency: string;
}

export interface RouteCodExpectedResult {
  stops: CodStopExpected[];
  expectedCents: number;
  currency: string;
  cashOrderIds: string[];
}

type TxClient = Prisma.TransactionClient;

export async function computeRouteCodExpected(
  tx: TxClient,
  deliveryRouteId: string,
  tenantId: string,
): Promise<RouteCodExpectedResult> {
  const deliveredStops = await tx.deliveryRouteStop.findMany({
    where: {
      deliveryRouteId,
      tenantId,
      status: DeliveryRouteStopStatus.DELIVERED,
    },
    select: { id: true, orderId: true },
  });

  if (deliveredStops.length === 0) {
    return { stops: [], expectedCents: 0, currency: 'USD', cashOrderIds: [] };
  }

  const orderIds = deliveredStops.map((stop) => stop.orderId);
  const orders = await tx.order.findMany({
    where: { id: { in: orderIds }, tenantId },
    select: { id: true, totalCents: true, currency: true, paymentMethod: true },
  });

  const orderById = new Map(orders.map((order) => [order.id, order]));
  const stops: CodStopExpected[] = [];
  const cashOrderIds: string[] = [];
  let expectedCents = 0;
  let currency: string | null = null;

  for (const stop of deliveredStops) {
    const order = orderById.get(stop.orderId);
    if (!order || order.paymentMethod !== PrismaPaymentMethod.CASH) {
      continue;
    }

    if (currency === null) {
      currency = order.currency;
    } else if (order.currency !== currency) {
      throw new Error('ROUTE_COD_CURRENCY_MISMATCH');
    }

    stops.push({
      stopId: stop.id,
      orderId: stop.orderId,
      expectedCents: order.totalCents,
      currency: order.currency,
    });
    cashOrderIds.push(order.id);
    expectedCents += order.totalCents;
  }

  if (currency === null) {
    const fallback = orders[0] as Order | undefined;
    currency = fallback?.currency ?? 'USD';
  }

  return {
    stops,
    expectedCents,
    currency,
    cashOrderIds,
  };
}
