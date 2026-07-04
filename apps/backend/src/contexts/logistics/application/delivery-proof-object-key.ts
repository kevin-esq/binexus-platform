import { randomUUID } from 'node:crypto';

import type { ConfirmDeliveryProofInput } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';

export type DeliveryProofUploadKind = 'PHOTO' | 'SIGNATURE';

export const PHOTO_CONTENT_TYPES = ['image/jpeg', 'image/png', 'image/webp'] as const;
export const SIGNATURE_CONTENT_TYPES = ['image/png', 'image/svg+xml'] as const;

export const DEFAULT_DELIVERY_PROOF_MAX_PHOTO_BYTES = 1_048_576;
export const DEFAULT_DELIVERY_PROOF_MAX_SIGNATURE_BYTES = 524_288;

const KIND_PREFIX: Record<DeliveryProofUploadKind, 'photo' | 'signature'> = {
  PHOTO: 'photo',
  SIGNATURE: 'signature',
};

export function kindToObjectKeyPrefix(kind: DeliveryProofUploadKind): 'photo' | 'signature' {
  return KIND_PREFIX[kind];
}

export function contentTypeExtension(contentType: string): string {
  switch (contentType) {
    case 'image/jpeg':
      return 'jpg';
    case 'image/png':
      return 'png';
    case 'image/webp':
      return 'webp';
    case 'image/svg+xml':
      return 'svg';
    default:
      throw new BadRequestException(`Unsupported content type: ${contentType}`);
  }
}

export function assertAllowedProofUpload(
  kind: DeliveryProofUploadKind,
  contentType: string,
  sizeBytes: number,
  limits: { maxPhotoBytes: number; maxSignatureBytes: number },
): void {
  if (!Number.isInteger(sizeBytes) || sizeBytes <= 0) {
    throw new BadRequestException('sizeBytes must be a positive integer.');
  }

  if (kind === 'PHOTO') {
    if (!(PHOTO_CONTENT_TYPES as readonly string[]).includes(contentType)) {
      throw new BadRequestException(
        `contentType must be one of ${PHOTO_CONTENT_TYPES.join(', ')} for PHOTO uploads.`,
      );
    }
    if (sizeBytes > limits.maxPhotoBytes) {
      throw new BadRequestException(
        `Photo upload exceeds max size of ${limits.maxPhotoBytes} bytes.`,
      );
    }
    return;
  }

  if (!(SIGNATURE_CONTENT_TYPES as readonly string[]).includes(contentType)) {
    throw new BadRequestException(
      `contentType must be one of ${SIGNATURE_CONTENT_TYPES.join(', ')} for SIGNATURE uploads.`,
    );
  }
  if (sizeBytes > limits.maxSignatureBytes) {
    throw new BadRequestException(
      `Signature upload exceeds max size of ${limits.maxSignatureBytes} bytes.`,
    );
  }
}

export function buildDeliveryProofObjectKeyForContentType(
  tenantId: string,
  deliveryRouteStopId: string,
  kind: DeliveryProofUploadKind,
  contentType: string,
): string {
  const prefix = kindToObjectKeyPrefix(kind);
  const ext = contentTypeExtension(contentType);
  return `tenants/${tenantId}/delivery-proofs/${deliveryRouteStopId}/${prefix}-${randomUUID()}.${ext}`;
}

export function isValidDeliveryProofObjectKey(
  tenantId: string,
  deliveryRouteStopId: string,
  kind: 'photo' | 'signature',
  objectKey: string,
): boolean {
  const prefix = `tenants/${tenantId}/delivery-proofs/${deliveryRouteStopId}/${kind}-`;
  if (!objectKey.startsWith(prefix)) return false;

  const suffix = objectKey.slice(prefix.length);
  return /^[0-9a-f-]+\.(jpg|jpeg|png|webp|svg)$/i.test(suffix);
}

export function validateProofObjectKeys(
  tenantId: string,
  deliveryRouteStopId: string,
  proof: ConfirmDeliveryProofInput,
): void {
  if (proof.photoObjectKey !== undefined) {
    if (
      !isValidDeliveryProofObjectKey(tenantId, deliveryRouteStopId, 'photo', proof.photoObjectKey)
    ) {
      throw new BadRequestException(
        'photoObjectKey is not a valid tenant-scoped delivery proof key.',
      );
    }
  }

  if (proof.signatureObjectKey !== undefined) {
    if (
      !isValidDeliveryProofObjectKey(
        tenantId,
        deliveryRouteStopId,
        'signature',
        proof.signatureObjectKey,
      )
    ) {
      throw new BadRequestException(
        'signatureObjectKey is not a valid tenant-scoped delivery proof key.',
      );
    }
  }
}
