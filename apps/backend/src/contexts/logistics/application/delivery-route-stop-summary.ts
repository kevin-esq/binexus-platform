import { type DeliveryRouteStopSummary } from '@binexus/types';
import type { DeliveryRouteStop } from '@prisma/client';

export function toDeliveryRouteStopSummary(row: DeliveryRouteStop): DeliveryRouteStopSummary {
  return {
    id: row.id,
    deliveryRouteId: row.deliveryRouteId,
    orderId: row.orderId as DeliveryRouteStopSummary['orderId'],
    sequence: row.sequence,
    status: row.status,
    deliveredAt: row.deliveredAt?.toISOString() ?? null,
  };
}
