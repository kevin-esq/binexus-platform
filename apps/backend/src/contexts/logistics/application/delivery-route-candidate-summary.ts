import { type DeliveryRouteCandidateSummary } from '@binexus/types';
import type { DeliveryRouteCandidate } from '@prisma/client';

export function toDeliveryRouteCandidateSummary(
  row: DeliveryRouteCandidate,
): DeliveryRouteCandidateSummary {
  return {
    id: row.id,
    orderId: row.orderId as DeliveryRouteCandidateSummary['orderId'],
    branchId: row.branchId as DeliveryRouteCandidateSummary['branchId'],
    status: row.status,
    deliveryRouteId: row.deliveryRouteId,
    createdAt: row.createdAt.toISOString(),
    updatedAt: row.updatedAt.toISOString(),
  };
}
