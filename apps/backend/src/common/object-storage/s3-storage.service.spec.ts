import { type ConfigService } from '@nestjs/config';
import { describe, expect, it, vi } from 'vitest';

vi.mock('@aws-sdk/client-s3', () => ({
  S3Client: vi.fn(),
  PutObjectCommand: vi.fn().mockImplementation((input) => input),
}));

vi.mock('@aws-sdk/s3-request-presigner', () => ({
  getSignedUrl: vi.fn().mockResolvedValue('https://minio.local/upload'),
}));

import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { PutObjectCommand } from '@aws-sdk/client-s3';

import { S3StorageService } from './s3-storage.service';

describe('S3StorageService', () => {
  it('creates presigned upload URLs with configured TTL', async () => {
    const config = {
      get: vi.fn((key: string, fallback?: string) => {
        const values: Record<string, string> = {
          S3_ENDPOINT: 'http://localhost:9000',
          S3_REGION: 'us-east-1',
          S3_ACCESS_KEY: 'binexus',
          S3_SECRET_KEY: 'binexus123',
          S3_BUCKET: 'binexus-dev',
          S3_PRESIGNED_UPLOAD_TTL_SECONDS: '900',
        };
        return values[key] ?? fallback;
      }),
    } as unknown as ConfigService;

    const service = new S3StorageService(config);
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
});
