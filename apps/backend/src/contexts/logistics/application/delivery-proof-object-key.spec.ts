import { BadRequestException } from '@nestjs/common';
import { describe, expect, it } from 'vitest';

import {
  assertAllowedProofUpload,
  buildDeliveryProofObjectKeyForContentType,
  contentTypeExtension,
  isValidDeliveryProofObjectKey,
  validateProofObjectKeys,
} from './delivery-proof-object-key';

describe('delivery-proof-object-key', () => {
  it('maps content types to file extensions', () => {
    expect(contentTypeExtension('image/jpeg')).toBe('jpg');
    expect(contentTypeExtension('image/png')).toBe('png');
    expect(contentTypeExtension('image/webp')).toBe('webp');
    expect(contentTypeExtension('image/svg+xml')).toBe('svg');
  });

  it('rejects unsupported content types', () => {
    expect(() => contentTypeExtension('application/pdf')).toThrow(BadRequestException);
  });

  it('builds tenant-scoped object keys', () => {
    const key = buildDeliveryProofObjectKeyForContentType(
      'tenant-1',
      'stop-1',
      'PHOTO',
      'image/jpeg',
    );

    expect(key).toMatch(/^tenants\/tenant-1\/delivery-proofs\/stop-1\/photo-[0-9a-f-]+\.jpg$/);
  });

  it('validates proof object key prefixes', () => {
    const key =
      'tenants/tenant-1/delivery-proofs/stop-1/photo-550e8400-e29b-41d4-a716-446655440000.jpg';

    expect(isValidDeliveryProofObjectKey('tenant-1', 'stop-1', 'photo', key)).toBe(true);
    expect(isValidDeliveryProofObjectKey('tenant-2', 'stop-1', 'photo', key)).toBe(false);
    expect(isValidDeliveryProofObjectKey('tenant-1', 'stop-2', 'photo', key)).toBe(false);
    expect(isValidDeliveryProofObjectKey('tenant-1', 'stop-1', 'signature', key)).toBe(false);
  });

  it('rejects invalid upload sizes and content types', () => {
    expect(() =>
      assertAllowedProofUpload('PHOTO', 'application/pdf', 100, {
        maxPhotoBytes: 1024,
        maxSignatureBytes: 512,
      }),
    ).toThrow(BadRequestException);

    expect(() =>
      assertAllowedProofUpload('PHOTO', 'image/jpeg', 2048, {
        maxPhotoBytes: 1024,
        maxSignatureBytes: 512,
      }),
    ).toThrow(BadRequestException);
  });

  it('validates confirm payload object keys', () => {
    const photoKey =
      'tenants/tenant-1/delivery-proofs/stop-1/photo-550e8400-e29b-41d4-a716-446655440000.jpg';

    expect(() =>
      validateProofObjectKeys('tenant-1', 'stop-1', { photoObjectKey: photoKey }),
    ).not.toThrow();

    expect(() =>
      validateProofObjectKeys('tenant-1', 'stop-1', { photoObjectKey: 'proofs/photo-1.jpg' }),
    ).toThrow(BadRequestException);
  });
});
