import type { ConfirmDeliveryProofInput } from '@binexus/types';
import type { DeliveryProof } from '@prisma/client';

import { type S3StorageService } from '../../../common/object-storage/s3-storage.service';

import { toDeliveryProofSummary } from './delivery-proof-summary';

export function hasProofInput(proof?: ConfirmDeliveryProofInput): boolean {
  if (!proof) return false;
  return (
    proof.recipientName !== undefined ||
    proof.notes !== undefined ||
    proof.photoObjectKey !== undefined ||
    proof.signatureObjectKey !== undefined ||
    proof.latitude !== undefined ||
    proof.longitude !== undefined
  );
}

export function toDeliveryConfirmedProofPayload(proof: DeliveryProof) {
  const summary = toDeliveryProofSummary(proof);
  return {
    recipientName: summary.recipientName ?? undefined,
    notes: summary.notes ?? undefined,
    photoObjectKey: summary.photoObjectKey ?? undefined,
    signatureObjectKey: summary.signatureObjectKey ?? undefined,
    latitude: summary.latitude ?? undefined,
    longitude: summary.longitude ?? undefined,
  };
}

export function proofCreateData(
  tenantId: string,
  deliveryRouteStopId: string,
  capturedByUserId: string,
  capturedAt: Date,
  proof: ConfirmDeliveryProofInput,
) {
  return {
    tenantId,
    deliveryRouteStopId,
    recipientName: proof.recipientName ?? null,
    notes: proof.notes ?? null,
    photoObjectKey: proof.photoObjectKey ?? null,
    signatureObjectKey: proof.signatureObjectKey ?? null,
    latitude: proof.latitude ?? null,
    longitude: proof.longitude ?? null,
    capturedByUserId,
    capturedAt,
  };
}

export function proofUpdateData(proof: ConfirmDeliveryProofInput) {
  return {
    recipientName: proof.recipientName ?? null,
    notes: proof.notes ?? null,
    photoObjectKey: proof.photoObjectKey ?? null,
    signatureObjectKey: proof.signatureObjectKey ?? null,
    latitude: proof.latitude ?? null,
    longitude: proof.longitude ?? null,
  };
}

export async function assertProofMediaExists(
  storage: S3StorageService,
  proof: ConfirmDeliveryProofInput,
): Promise<void> {
  if (proof.photoObjectKey !== undefined) {
    await storage.assertObjectExists(proof.photoObjectKey, 'photoObjectKey');
  }
  if (proof.signatureObjectKey !== undefined) {
    await storage.assertObjectExists(proof.signatureObjectKey, 'signatureObjectKey');
  }
}
