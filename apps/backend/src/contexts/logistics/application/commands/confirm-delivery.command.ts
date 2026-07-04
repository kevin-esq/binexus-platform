import { DomainEventName, type DeliveryConfirmedPayload } from '@binexus/events';
import {
  type ConfirmDeliveryProofInput,
  type ConfirmDeliveryResult,
  type DeliveryRouteStatus,
  type OrderId,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus as PrismaRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';
import type { DeliveryProof, Prisma } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { S3StorageService } from '../../../../common/object-storage/s3-storage.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import {
  assertProofMediaExists,
  hasProofInput,
  proofCreateData,
  proofUpdateData,
  toDeliveryConfirmedProofPayload,
} from '../confirm-delivery-proof';
import { validateProofObjectKeys } from '../delivery-proof-object-key';
import { toDeliveryProofSummary } from '../delivery-proof-summary';

export class ConfirmDeliveryCommand extends AppCommand<ConfirmDeliveryResult> {
  constructor(
    readonly deliveryRouteStopId: string,
    readonly issuedBy: UserId,
    readonly proof?: ConfirmDeliveryProofInput,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteStopId.trim()) {
      throw new BadRequestException('deliveryRouteStopId is required.');
    }
  }
}

type TxClient = Prisma.TransactionClient;

@Injectable()
@CommandHandler(ConfirmDeliveryCommand)
export class ConfirmDeliveryHandler extends AppCommandHandler<ConfirmDeliveryCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
    @Inject(EventBusService)
    private readonly eventBus: EventBusService,
    @Inject(OutboxService)
    private readonly outbox: OutboxService,
    @Inject(S3StorageService)
    private readonly storage: S3StorageService,
  ) {
    super();
  }

  async execute(command: ConfirmDeliveryCommand): Promise<ConfirmDeliveryResult> {
    const ctx = this.tenantContext.current();

    if (command.proof) {
      validateProofObjectKeys(ctx.tenantId, command.deliveryRouteStopId, command.proof);
      await assertProofMediaExists(this.storage, command.proof);
    }

    return this.prisma.$transaction(async (tx) => {
      const stop = await tx.deliveryRouteStop.findFirst({
        where: { id: command.deliveryRouteStopId, tenantId: ctx.tenantId },
        include: { deliveryRoute: true, deliveryProof: true },
      });

      if (!stop) {
        throw new NotFoundException(`Delivery route stop ${command.deliveryRouteStopId} not found`);
      }

      const route = stop.deliveryRoute;

      if (stop.status === DeliveryRouteStopStatus.DELIVERED) {
        const proof = await this.upsertProofIfNeeded(
          tx,
          ctx.tenantId,
          stop.id,
          command.issuedBy,
          stop.deliveredAt ?? new Date(),
          command.proof,
          stop.deliveryProof,
        );

        return {
          deliveryRouteStopId: stop.id,
          orderId: stop.orderId as OrderId,
          status: 'DELIVERED',
          deliveredAt: (stop.deliveredAt ?? new Date()).toISOString(),
          routeStatus: route.status as DeliveryRouteStatus,
          proof: proof ? toDeliveryProofSummary(proof) : null,
        };
      }

      if (stop.status !== DeliveryRouteStopStatus.PLANNED) {
        throw new BadRequestException(
          `Delivery route stop ${command.deliveryRouteStopId} cannot be confirmed (status=${stop.status})`,
        );
      }

      if (route.status !== PrismaRouteStatus.DISPATCHED) {
        throw new BadRequestException(
          `Delivery route ${route.id} is not dispatched (status=${route.status})`,
        );
      }

      const deliveredAt = new Date();

      await tx.deliveryRouteStop.update({
        where: { id: stop.id },
        data: {
          status: DeliveryRouteStopStatus.DELIVERED,
          deliveredAt,
          deliveredByUserId: command.issuedBy,
        },
      });

      const savedProof = await this.upsertProofIfNeeded(
        tx,
        ctx.tenantId,
        stop.id,
        command.issuedBy,
        deliveredAt,
        command.proof,
        stop.deliveryProof,
      );

      const pendingStops = await tx.deliveryRouteStop.count({
        where: {
          deliveryRouteId: route.id,
          tenantId: ctx.tenantId,
          status: { not: DeliveryRouteStopStatus.DELIVERED },
        },
      });

      let routeStatus = route.status as DeliveryRouteStatus;

      if (pendingStops === 0) {
        const completed = await tx.deliveryRoute.update({
          where: { id: route.id },
          data: {
            status: PrismaRouteStatus.COMPLETED,
            completedAt: deliveredAt,
          },
        });
        routeStatus = completed.status as DeliveryRouteStatus;
      }

      const eventPayload: DeliveryConfirmedPayload = {
        deliveryRouteId: route.id,
        deliveryRouteStopId: stop.id,
        branchId: route.branchId,
        orderId: stop.orderId,
        confirmedBy: command.issuedBy,
        confirmedAt: deliveredAt.toISOString(),
        ...(savedProof ? { proof: toDeliveryConfirmedProofPayload(savedProof) } : {}),
      };

      const event = this.eventBus.build(DomainEventName.DELIVERY_CONFIRMED, eventPayload, {
        correlationId: command.correlationId,
        causationId: command.causationId ?? command.commandId,
      });

      await this.outbox.record(event, tx);

      return {
        deliveryRouteStopId: stop.id,
        orderId: stop.orderId as OrderId,
        status: 'DELIVERED',
        deliveredAt: deliveredAt.toISOString(),
        routeStatus,
        proof: savedProof ? toDeliveryProofSummary(savedProof) : null,
      };
    });
  }

  private async upsertProofIfNeeded(
    tx: TxClient,
    tenantId: string,
    deliveryRouteStopId: string,
    capturedByUserId: string,
    capturedAt: Date,
    proofInput: ConfirmDeliveryProofInput | undefined,
    existing: DeliveryProof | null,
  ): Promise<DeliveryProof | null> {
    if (!hasProofInput(proofInput)) {
      return existing;
    }

    const input = proofInput!;

    if (existing) {
      return tx.deliveryProof.update({
        where: { id: existing.id },
        data: proofUpdateData(input),
      });
    }

    return tx.deliveryProof.create({
      data: proofCreateData(tenantId, deliveryRouteStopId, capturedByUserId, capturedAt, input),
    });
  }
}
