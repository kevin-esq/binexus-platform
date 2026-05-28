import { type DeliveryProofSummary } from '@binexus/types';
import type { DeliveryProof } from '@prisma/client';

export function toDeliveryProofSummary(row: DeliveryProof): DeliveryProofSummary {
  return {
    id: row.id,
    recipientName: row.recipientName,
    notes: row.notes,
    photoObjectKey: row.photoObjectKey,
    signatureObjectKey: row.signatureObjectKey,
    latitude: row.latitude,
    longitude: row.longitude,
    capturedAt: row.capturedAt.toISOString(),
  };
}
