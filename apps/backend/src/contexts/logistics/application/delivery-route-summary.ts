import { type DeliveryRouteSummary } from '@binexus/types';
import type { DeliveryRoute } from '@prisma/client';

export function toDeliveryRouteSummary(
  row: DeliveryRoute & { _count?: { stops: number }; stops?: unknown[] },
  stopCount?: number,
): DeliveryRouteSummary {
  const count = stopCount ?? row._count?.stops ?? (Array.isArray(row.stops) ? row.stops.length : 0);

  return {
    id: row.id,
    branchId: row.branchId as DeliveryRouteSummary['branchId'],
    status: row.status,
    driverUserId: row.driverUserId as DeliveryRouteSummary['driverUserId'],
    plannedDate: row.plannedDate?.toISOString() ?? null,
    dispatchedAt: row.dispatchedAt?.toISOString() ?? null,
    completedAt: row.completedAt?.toISOString() ?? null,
    stopCount: count,
    createdAt: row.createdAt.toISOString(),
    updatedAt: row.updatedAt.toISOString(),
  };
}
