import type { DeliveryRouteStopCounts } from '@binexus/types';
import {
  DeliveryRouteStatus as PrismaRouteStatus,
  DeliveryRouteStopStatus,
  type DeliveryRoute,
  type Prisma,
} from '@prisma/client';

export const TERMINAL_STOP_STATUSES: readonly DeliveryRouteStopStatus[] = [
  DeliveryRouteStopStatus.DELIVERED,
  DeliveryRouteStopStatus.FAILED,
  DeliveryRouteStopStatus.SKIPPED,
];

type TxClient = Prisma.TransactionClient;

export async function countNonTerminalStops(
  tx: TxClient,
  deliveryRouteId: string,
  tenantId: string,
): Promise<number> {
  return tx.deliveryRouteStop.count({
    where: {
      deliveryRouteId,
      tenantId,
      status: { notIn: [...TERMINAL_STOP_STATUSES] },
    },
  });
}

export async function completeRouteIfAllTerminal(
  tx: TxClient,
  route: Pick<DeliveryRoute, 'id' | 'tenantId' | 'status'>,
  completedAt: Date,
): Promise<PrismaRouteStatus> {
  const pending = await countNonTerminalStops(tx, route.id, route.tenantId);
  if (pending > 0) {
    return route.status;
  }

  const completed = await tx.deliveryRoute.update({
    where: { id: route.id },
    data: {
      status: PrismaRouteStatus.COMPLETED,
      completedAt,
    },
  });

  return completed.status;
}

export async function getRouteStopCounts(
  tx: TxClient,
  deliveryRouteId: string,
  tenantId: string,
): Promise<DeliveryRouteStopCounts> {
  const grouped = await tx.deliveryRouteStop.groupBy({
    by: ['status'],
    where: { deliveryRouteId, tenantId },
    _count: { _all: true },
  });

  const counts: DeliveryRouteStopCounts = {
    planned: 0,
    delivered: 0,
    failed: 0,
    skipped: 0,
  };

  for (const row of grouped) {
    switch (row.status) {
      case DeliveryRouteStopStatus.PLANNED:
        counts.planned = row._count._all;
        break;
      case DeliveryRouteStopStatus.DELIVERED:
        counts.delivered = row._count._all;
        break;
      case DeliveryRouteStopStatus.FAILED:
        counts.failed = row._count._all;
        break;
      case DeliveryRouteStopStatus.SKIPPED:
        counts.skipped = row._count._all;
        break;
    }
  }

  return counts;
}
