import { type DeliveryRouteStopSummary } from '@binexus/types';
import type { DeliveryProof, DeliveryRouteStop } from '@prisma/client';

import { toDeliveryProofSummary } from './delivery-proof-summary';

export type DeliveryRouteStopWithProof = DeliveryRouteStop & {
  deliveryProof?: DeliveryProof | null;
};

export function toDeliveryRouteStopSummary(
  row: DeliveryRouteStopWithProof,
): DeliveryRouteStopSummary {
  return {
    id: row.id,
    deliveryRouteId: row.deliveryRouteId,
    orderId: row.orderId as DeliveryRouteStopSummary['orderId'],
    sequence: row.sequence,
    status: row.status,
    deliveredAt: row.deliveredAt?.toISOString() ?? null,
    proof: row.deliveryProof ? toDeliveryProofSummary(row.deliveryProof) : null,
  };
}
