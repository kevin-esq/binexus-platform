import { BadRequestException, ServiceUnavailableException } from '@nestjs/common';
import { type ConfigService } from '@nestjs/config';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const sendMock = vi.fn();

vi.mock('@aws-sdk/client-s3', () => ({
  S3Client: vi.fn().mockImplementation(() => ({ send: sendMock })),
  PutObjectCommand: vi.fn().mockImplementation((input) => input),
  HeadObjectCommand: vi.fn().mockImplementation((input) => input),
}));

vi.mock('@aws-sdk/s3-request-presigner', () => ({
  getSignedUrl: vi.fn().mockResolvedValue('https://minio.local/upload'),
}));

vi.mock('@smithy/node-http-handler', () => ({
  NodeHttpHandler: vi.fn().mockImplementation(() => ({})),
}));

import { HeadObjectCommand, PutObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { NodeHttpHandler } from '@smithy/node-http-handler';

import { S3StorageService } from './s3-storage.service';

function makeConfig(overrides: Record<string, string> = {}) {
  const values: Record<string, string> = {
    S3_ENDPOINT: 'http://localhost:9000',
    S3_REGION: 'us-east-1',
    S3_ACCESS_KEY: 'binexus',
    S3_SECRET_KEY: 'binexus123',
    S3_BUCKET: 'binexus-dev',
    S3_PRESIGNED_UPLOAD_TTL_SECONDS: '900',
    S3_REQUEST_TIMEOUT_MS: '5000',
    ...overrides,
  };

  return {
    get: vi.fn((key: string, fallback?: string) => values[key] ?? fallback),
  } as unknown as ConfigService;
}

describe('S3StorageService', () => {
  beforeEach(() => {
    sendMock.mockReset();
  });

  it('configures S3 client with 3s connection and 5s request timeouts', () => {
    new S3StorageService(makeConfig());

    expect(NodeHttpHandler).toHaveBeenCalledWith({
      connectionTimeout: 3_000,
      requestTimeout: 5_000,
    });
  });

  it('creates presigned upload URLs with configured TTL', async () => {
    const service = new S3StorageService(makeConfig());
    const result = await service.createPresignedUploadUrl(
      'tenants/t1/delivery-proofs/stop-1/photo-1.jpg',
      'image/jpeg',
    );

    expect(PutObjectCommand).toHaveBeenCalledWith({
      Bucket: 'binexus-dev',
      Key: 'tenants/t1/delivery-proofs/stop-1/photo-1.jpg',
      ContentType: 'image/jpeg',
    });
    expect(getSignedUrl).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ Bucket: 'binexus-dev' }),
      { expiresIn: 900 },
    );
    expect(result.uploadUrl).toBe('https://minio.local/upload');
    expect(result.expiresAt).toMatch(/^\d{4}-\d{2}-\d{2}T/);
  });

  it('assertObjectExists succeeds when HeadObject returns', async () => {
    sendMock.mockResolvedValue({});
    const service = new S3StorageService(makeConfig());

    await expect(
      service.assertObjectExists('tenants/t1/delivery-proofs/stop-1/photo-1.jpg', 'photoObjectKey'),
    ).resolves.toBeUndefined();

    expect(HeadObjectCommand).toHaveBeenCalledWith({
      Bucket: 'binexus-dev',
      Key: 'tenants/t1/delivery-proofs/stop-1/photo-1.jpg',
    });
  });

  it('assertObjectExists rejects when object is missing', async () => {
    sendMock.mockRejectedValue({ name: 'NotFound', $metadata: { httpStatusCode: 404 } });
    const service = new S3StorageService(makeConfig());

    await expect(
      service.assertObjectExists('tenants/t1/delivery-proofs/stop-1/photo-1.jpg', 'photoObjectKey'),
    ).rejects.toBeInstanceOf(BadRequestException);
  });

  it('assertObjectExists rejects when object storage is unavailable', async () => {
    sendMock.mockRejectedValue({ name: 'TimeoutError', code: 'ETIMEDOUT' });
    const service = new S3StorageService(makeConfig());

    await expect(
      service.assertObjectExists('tenants/t1/delivery-proofs/stop-1/photo-1.jpg', 'photoObjectKey'),
    ).rejects.toBeInstanceOf(ServiceUnavailableException);
  });
});
