import { DomainEventName } from '@binexus/events';
import type { UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { type ConfigService } from '@nestjs/config';
import { DeliveryRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';
import { beforeAll, describe, expect, it, vi } from 'vitest';

import { type EventBusService } from '../../common/events/event-bus.service';
import { type OutboxService } from '../../common/events/outbox.service';
import { S3StorageService } from '../../common/object-storage/s3-storage.service';
import { type PrismaService } from '../../common/prisma/prisma.service';
import { type TenantContextService } from '../../common/tenant/tenant-context.service';
import {
  ConfirmDeliveryCommand,
  ConfirmDeliveryHandler,
} from '../../contexts/logistics/application/commands/confirm-delivery.command';
import {
  CreateDeliveryProofUploadCommand,
  CreateDeliveryProofUploadHandler,
} from '../../contexts/logistics/application/commands/create-delivery-proof-upload.command';
import { assertProofMediaExists } from '../../contexts/logistics/application/confirm-delivery-proof';
import { requireMinIo } from '../helpers/require-minio';

const TENANT_ID = 'integration-test-tenant';
const STOP_ID = 'integration-stop-1';
const USER_ID = 'integration-user-1' as UserId;
const CONTENT_TYPE = 'image/jpeg';
const PROOF_BYTES = Buffer.from([0xff, 0xd8, 0xff, 0xd9]);

const tenantContext = {
  tenantId: TENANT_ID,
  userId: USER_ID,
  role: 'ADMIN',
  branchId: 'integration-branch-1',
  requestId: 'integration-request-1',
};

function makeStorageService(): S3StorageService {
  const config = {
    get: (key: string, defaultValue?: string) => process.env[key] ?? defaultValue,
  } as ConfigService;
  return new S3StorageService(config);
}

function makeDispatchedStop() {
  return {
    id: STOP_ID,
    tenantId: TENANT_ID,
    deliveryRouteId: 'integration-route-1',
    orderId: 'integration-order-1',
    sequence: 1,
    status: DeliveryRouteStopStatus.PLANNED,
    deliveredAt: null,
    deliveryProof: null,
    deliveryRoute: {
      id: 'integration-route-1',
      branchId: 'integration-branch-1',
      status: DeliveryRouteStatus.DISPATCHED,
      completedAt: null,
    },
  };
}

async function putViaPresignedUrl(
  uploadUrl: string,
  contentType: string,
  body: Buffer,
): Promise<void> {
  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body,
  });
  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(`Presigned PUT failed: HTTP ${response.status} ${text}`);
  }
}

async function presignPhotoUpload(
  storage: S3StorageService,
): Promise<{ objectKey: string; uploadUrl: string }> {
  const prisma = {
    deliveryRouteStop: {
      findFirst: vi.fn().mockResolvedValue(makeDispatchedStop()),
    },
  } as unknown as PrismaService;

  const handler = new CreateDeliveryProofUploadHandler(
    prisma,
    { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
    storage,
  );

  const result = await handler.execute(
    new CreateDeliveryProofUploadCommand(STOP_ID, 'PHOTO', CONTENT_TYPE, PROOF_BYTES.length),
  );

  return { objectKey: result.objectKey, uploadUrl: result.uploadUrl };
}

function makeConfirmHandler(storage: S3StorageService, tx: Record<string, unknown>) {
  const prisma = {
    $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
  } as unknown as PrismaService;

  return {
    handler: new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      { build: vi.fn().mockReturnValue({ id: 'evt-integration-1' }) } as unknown as EventBusService,
      { record: vi.fn().mockResolvedValue(undefined) } as unknown as OutboxService,
      storage,
    ),
    prisma,
    tx,
  };
}

describe('requireMinIo preflight', () => {
  it('rejects unreachable endpoint with actionable message', async () => {
    await expect(requireMinIo('http://127.0.0.1:59999')).rejects.toThrow(
      /Integration tests require MinIO\. Start it with: pnpm docker:up/,
    );
    await expect(requireMinIo('http://127.0.0.1:59999')).rejects.toThrow(
      /S3_ENDPOINT=http:\/\/127\.0\.0\.1:59999/,
    );
  });
});

describe('delivery proof MinIO integration', () => {
  let storage: S3StorageService;

  beforeAll(async () => {
    await requireMinIo();
    storage = makeStorageService();
  });

  it('presigns, PUTs to MinIO, and HeadObject succeeds', async () => {
    const { objectKey, uploadUrl } = await presignPhotoUpload(storage);

    await putViaPresignedUrl(uploadUrl, CONTENT_TYPE, PROOF_BYTES);

    await expect(storage.assertObjectExists(objectKey, 'photoObjectKey')).resolves.toBeUndefined();
  });

  it('HeadObject on missing object returns BadRequestException from real MinIO', async () => {
    const missingKey = `tenants/${TENANT_ID}/delivery-proofs/${STOP_ID}/photo-integration-missing-${Date.now()}.jpg`;

    await expect(storage.assertObjectExists(missingKey, 'photoObjectKey')).rejects.toBeInstanceOf(
      BadRequestException,
    );
    await expect(storage.assertObjectExists(missingKey, 'photoObjectKey')).rejects.toThrow(
      'photoObjectKey was not found in object storage.',
    );
  });

  it('assertProofMediaExists passes when object was uploaded', async () => {
    const { objectKey, uploadUrl } = await presignPhotoUpload(storage);
    await putViaPresignedUrl(uploadUrl, CONTENT_TYPE, PROOF_BYTES);

    await expect(
      assertProofMediaExists(storage, { photoObjectKey: objectKey }),
    ).resolves.toBeUndefined();
  });

  it('assertProofMediaExists rejects key that was never uploaded', async () => {
    const fakeKey = `tenants/${TENANT_ID}/delivery-proofs/${STOP_ID}/photo-integration-never-uploaded-${Date.now()}.jpg`;

    await expect(
      assertProofMediaExists(storage, { photoObjectKey: fakeKey }),
    ).rejects.toBeInstanceOf(BadRequestException);
    await expect(assertProofMediaExists(storage, { photoObjectKey: fakeKey })).rejects.toThrow(
      'photoObjectKey was not found in object storage.',
    );
  });

  it('ConfirmDeliveryHandler aborts before transaction when proof object is missing in MinIO', async () => {
    const { objectKey } = await presignPhotoUpload(storage);

    const tx = {
      deliveryRouteStop: { findFirst: vi.fn(), update: vi.fn(), count: vi.fn() },
      deliveryRoute: { update: vi.fn() },
      deliveryProof: { create: vi.fn(), update: vi.fn() },
    };
    const { handler, prisma } = makeConfirmHandler(storage, tx);

    await expect(
      handler.execute(
        new ConfirmDeliveryCommand(STOP_ID, USER_ID, {
          photoObjectKey: objectKey,
          notes: 'Should not persist',
        }),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);

    expect(prisma.$transaction).not.toHaveBeenCalled();
  });

  it('ConfirmDeliveryHandler confirms delivery when proof exists in MinIO', async () => {
    const { objectKey, uploadUrl } = await presignPhotoUpload(storage);
    await putViaPresignedUrl(uploadUrl, CONTENT_TYPE, PROOF_BYTES);

    const savedProof = {
      id: 'proof-integration-1',
      tenantId: TENANT_ID,
      deliveryRouteStopId: STOP_ID,
      recipientName: null,
      notes: 'Proof uploaded via integration test',
      photoObjectKey: objectKey,
      signatureObjectKey: null,
      latitude: null,
      longitude: null,
      capturedByUserId: USER_ID,
      capturedAt: new Date(),
      createdAt: new Date(),
      updatedAt: new Date(),
    };

    const stop = makeDispatchedStop();
    const tx = {
      deliveryRouteStop: {
        findFirst: vi.fn().mockResolvedValue(stop),
        update: vi.fn(),
        count: vi.fn().mockResolvedValue(0),
      },
      deliveryRoute: {
        update: vi.fn().mockResolvedValue({
          id: 'integration-route-1',
          status: DeliveryRouteStatus.COMPLETED,
        }),
      },
      deliveryProof: {
        create: vi.fn().mockResolvedValue(savedProof),
        update: vi.fn(),
      },
    };

    const event = {
      id: 'evt-integration-confirm',
      name: DomainEventName.DELIVERY_CONFIRMED,
      tenantId: TENANT_ID,
      occurredAt: new Date().toISOString(),
      version: 1,
      payload: {},
    };
    const eventBus = { build: vi.fn().mockReturnValue(event) };
    const outbox = { record: vi.fn().mockResolvedValue(undefined) };

    const prisma = {
      $transaction: vi.fn((cb: (client: typeof tx) => Promise<unknown>) => cb(tx)),
    } as unknown as PrismaService;

    const handler = new ConfirmDeliveryHandler(
      prisma,
      { current: vi.fn().mockReturnValue(tenantContext) } as unknown as TenantContextService,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
      storage,
    );

    const result = await handler.execute(
      new ConfirmDeliveryCommand(STOP_ID, USER_ID, {
        photoObjectKey: objectKey,
        notes: 'Proof uploaded via integration test',
      }),
    );

    expect(prisma.$transaction).toHaveBeenCalled();
    expect(tx.deliveryProof.create).toHaveBeenCalled();
    expect(result.status).toBe('DELIVERED');
    expect(result.proof?.photoObjectKey).toBe(objectKey);
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.DELIVERY_CONFIRMED,
      expect.objectContaining({
        proof: expect.objectContaining({ photoObjectKey: objectKey }),
      }),
      expect.any(Object),
    );
    expect(outbox.record).toHaveBeenCalledWith(event, tx);
  });
});
