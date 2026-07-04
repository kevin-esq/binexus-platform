import { type CreateDeliveryProofUploadResult, type DeliveryProofUploadKind } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { S3StorageService } from '../../../../common/object-storage/s3-storage.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import {
  assertAllowedProofUpload,
  buildDeliveryProofObjectKeyForContentType,
  DEFAULT_DELIVERY_PROOF_MAX_PHOTO_BYTES,
  DEFAULT_DELIVERY_PROOF_MAX_SIGNATURE_BYTES,
} from '../delivery-proof-object-key';

export class CreateDeliveryProofUploadCommand extends AppCommand<CreateDeliveryProofUploadResult> {
  constructor(
    readonly deliveryRouteStopId: string,
    readonly kind: DeliveryProofUploadKind,
    readonly contentType: string,
    readonly sizeBytes: number,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteStopId.trim()) {
      throw new BadRequestException('deliveryRouteStopId is required.');
    }

    if (this.kind !== 'PHOTO' && this.kind !== 'SIGNATURE') {
      throw new BadRequestException('kind must be PHOTO or SIGNATURE.');
    }

    assertAllowedProofUpload(this.kind, this.contentType, this.sizeBytes, {
      maxPhotoBytes: Number(
        process.env.DELIVERY_PROOF_MAX_PHOTO_BYTES ?? DEFAULT_DELIVERY_PROOF_MAX_PHOTO_BYTES,
      ),
      maxSignatureBytes: Number(
        process.env.DELIVERY_PROOF_MAX_SIGNATURE_BYTES ??
          DEFAULT_DELIVERY_PROOF_MAX_SIGNATURE_BYTES,
      ),
    });
  }
}

@Injectable()
@CommandHandler(CreateDeliveryProofUploadCommand)
export class CreateDeliveryProofUploadHandler extends AppCommandHandler<CreateDeliveryProofUploadCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
    @Inject(S3StorageService)
    private readonly storage: S3StorageService,
  ) {
    super();
  }

  async execute(
    command: CreateDeliveryProofUploadCommand,
  ): Promise<CreateDeliveryProofUploadResult> {
    const ctx = this.tenantContext.current();

    const stop = await this.prisma.deliveryRouteStop.findFirst({
      where: { id: command.deliveryRouteStopId, tenantId: ctx.tenantId },
      include: { deliveryRoute: true },
    });

    if (!stop) {
      throw new NotFoundException(`Delivery route stop ${command.deliveryRouteStopId} not found`);
    }

    if (stop.status !== DeliveryRouteStopStatus.PLANNED) {
      throw new BadRequestException(
        `Delivery route stop ${command.deliveryRouteStopId} is not awaiting delivery (status=${stop.status})`,
      );
    }

    if (stop.deliveryRoute.status !== DeliveryRouteStatus.DISPATCHED) {
      throw new BadRequestException(
        `Delivery route ${stop.deliveryRoute.id} is not dispatched (status=${stop.deliveryRoute.status})`,
      );
    }

    const objectKey = buildDeliveryProofObjectKeyForContentType(
      ctx.tenantId,
      stop.id,
      command.kind,
      command.contentType,
    );

    const presigned = await this.storage.createPresignedUploadUrl(objectKey, command.contentType);

    return {
      objectKey,
      uploadUrl: presigned.uploadUrl,
      expiresAt: presigned.expiresAt,
    };
  }
}
