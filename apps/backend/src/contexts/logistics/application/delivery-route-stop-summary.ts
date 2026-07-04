import { type DeliveryRouteStopSummary, type DeliveryFailureReason } from '@binexus/types';
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
    failedAt: row.failedAt?.toISOString() ?? null,
    failureReason: (row.failureReason as DeliveryFailureReason | null) ?? null,
    failureNotes: row.failureNotes ?? null,
    proof: row.deliveryProof ? toDeliveryProofSummary(row.deliveryProof) : null,
  };
}
