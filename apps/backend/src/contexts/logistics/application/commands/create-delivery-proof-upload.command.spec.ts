import { BadRequestException, NotFoundException } from '@nestjs/common';
import { DeliveryRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type S3StorageService } from '../../../../common/object-storage/s3-storage.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import {
  CreateDeliveryProofUploadCommand,
  CreateDeliveryProofUploadHandler,
} from './create-delivery-proof-upload.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

function makeStop(overrides: Record<string, unknown> = {}) {
  return {
    id: 'stop-1',
    tenantId: 'tenant-1',
    deliveryRouteId: 'route-1',
    orderId: 'order-1',
    sequence: 1,
    status: DeliveryRouteStopStatus.PLANNED,
    deliveryRoute: {
      id: 'route-1',
      status: DeliveryRouteStatus.DISPATCHED,
    },
    ...overrides,
  };
}

describe('CreateDeliveryProofUploadHandler', () => {
  it('returns presigned upload metadata for a planned stop on a dispatched route', async () => {
    const stop = makeStop();
    const prisma = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
      },
    } as unknown as PrismaService;
    const storage = {
      createPresignedUploadUrl: vi.fn().mockResolvedValue({
        uploadUrl: 'https://minio.local/upload',
        expiresAt: '2026-05-25T18:00:00.000Z',
      }),
    } as unknown as S3StorageService;

    const handler = new CreateDeliveryProofUploadHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      storage,
    );

    const result = await handler.execute(
      new CreateDeliveryProofUploadCommand('stop-1', 'PHOTO', 'image/jpeg', 1024),
    );

    expect(result.uploadUrl).toBe('https://minio.local/upload');
    expect(result.objectKey).toMatch(
      /^tenants\/tenant-1\/delivery-proofs\/stop-1\/photo-[0-9a-f-]+\.jpg$/,
    );
    expect(storage.createPresignedUploadUrl).toHaveBeenCalledWith(result.objectKey, 'image/jpeg');
  });

  it('rejects invalid upload metadata in command validation', () => {
    expect(() =>
      new CreateDeliveryProofUploadCommand('stop-1', 'PHOTO', 'application/pdf', 100).validate(),
    ).toThrow(BadRequestException);
  });

  it('rejects when stop is not found', async () => {
    const prisma = {
      deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(null) },
    } as unknown as PrismaService;

    const handler = new CreateDeliveryProofUploadHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { createPresignedUploadUrl: vi.fn() } as unknown as S3StorageService,
    );

    await expect(
      handler.execute(new CreateDeliveryProofUploadCommand('stop-1', 'PHOTO', 'image/jpeg', 100)),
    ).rejects.toBeInstanceOf(NotFoundException);
  });

  it('rejects when route is not dispatched', async () => {
    const stop = makeStop({
      deliveryRoute: { id: 'route-1', status: DeliveryRouteStatus.PLANNED },
    });
    const prisma = {
      deliveryRouteStop: { findFirst: vi.fn().mockResolvedValue(stop) },
    } as unknown as PrismaService;

    const handler = new CreateDeliveryProofUploadHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { createPresignedUploadUrl: vi.fn() } as unknown as S3StorageService,
    );

    await expect(
      handler.execute(new CreateDeliveryProofUploadCommand('stop-1', 'PHOTO', 'image/jpeg', 100)),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});
