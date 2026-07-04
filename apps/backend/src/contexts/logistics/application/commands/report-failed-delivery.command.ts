import { DomainEventName, type DeliveryFailedPayload } from '@binexus/events';
import {
  type DeliveryFailureReason,
  type DeliveryRouteStatus,
  type OrderId,
  type ReportFailedDeliveryResult,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import {
  type DeliveryFailureReason as PrismaFailureReason,
  DeliveryRouteStatus as PrismaRouteStatus,
  DeliveryRouteStopStatus,
} from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { completeRouteIfAllTerminal, getRouteStopCounts } from '../route-completion';

export class ReportFailedDeliveryCommand extends AppCommand<ReportFailedDeliveryResult> {
  constructor(
    readonly deliveryRouteStopId: string,
    readonly issuedBy: UserId,
    readonly reason: DeliveryFailureReason,
    readonly notes?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteStopId.trim()) {
      throw new BadRequestException('deliveryRouteStopId is required.');
    }
    if (!this.reason.trim()) {
      throw new BadRequestException('reason is required.');
    }
  }
}

@Injectable()
@CommandHandler(ReportFailedDeliveryCommand)
export class ReportFailedDeliveryHandler extends AppCommandHandler<ReportFailedDeliveryCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
    @Inject(EventBusService)
    private readonly eventBus: EventBusService,
    @Inject(OutboxService)
    private readonly outbox: OutboxService,
  ) {
    super();
  }

  async execute(command: ReportFailedDeliveryCommand): Promise<ReportFailedDeliveryResult> {
    const ctx = this.tenantContext.current();
    const notes = command.notes?.trim() || undefined;

    return this.prisma.$transaction(async (tx) => {
      const stop = await tx.deliveryRouteStop.findFirst({
        where: { id: command.deliveryRouteStopId, tenantId: ctx.tenantId },
        include: { deliveryRoute: true },
      });

      if (!stop) {
        throw new NotFoundException(`Delivery route stop ${command.deliveryRouteStopId} not found`);
      }

      const route = stop.deliveryRoute;

      if (stop.status === DeliveryRouteStopStatus.FAILED) {
        const routeStatus = route.status as DeliveryRouteStatus;
        const routeStopCounts = await getRouteStopCounts(tx, route.id, ctx.tenantId);

        return {
          deliveryRouteStopId: stop.id,
          orderId: stop.orderId as OrderId,
          status: 'FAILED',
          failedAt: (stop.failedAt ?? new Date()).toISOString(),
          failureReason: stop.failureReason as DeliveryFailureReason,
          routeStatus,
          routeStopCounts,
        };
      }

      if (stop.status !== DeliveryRouteStopStatus.PLANNED) {
        throw new BadRequestException(
          `Delivery route stop ${command.deliveryRouteStopId} cannot be marked failed (status=${stop.status})`,
        );
      }

      if (route.status !== PrismaRouteStatus.DISPATCHED) {
        throw new BadRequestException(
          `Delivery route ${route.id} is not dispatched (status=${route.status})`,
        );
      }

      const failedAt = new Date();

      await tx.deliveryRouteStop.update({
        where: { id: stop.id },
        data: {
          status: DeliveryRouteStopStatus.FAILED,
          failedAt,
          failedByUserId: command.issuedBy,
          failureReason: command.reason as PrismaFailureReason,
          failureNotes: notes ?? null,
        },
      });

      const routeStatus = (await completeRouteIfAllTerminal(
        tx,
        route,
        failedAt,
      )) as DeliveryRouteStatus;
      const routeStopCounts = await getRouteStopCounts(tx, route.id, ctx.tenantId);

      const eventPayload: DeliveryFailedPayload = {
        deliveryRouteId: route.id,
        deliveryRouteStopId: stop.id,
        branchId: route.branchId,
        orderId: stop.orderId,
        failureReason: command.reason,
        ...(notes ? { failureNotes: notes } : {}),
        reportedBy: command.issuedBy,
        reportedAt: failedAt.toISOString(),
      };

      const event = this.eventBus.build(DomainEventName.DELIVERY_FAILED, eventPayload, {
        correlationId: command.correlationId,
        causationId: command.causationId ?? command.commandId,
      });

      await this.outbox.record(event, tx);

      return {
        deliveryRouteStopId: stop.id,
        orderId: stop.orderId as OrderId,
        status: 'FAILED',
        failedAt: failedAt.toISOString(),
        failureReason: command.reason,
        routeStatus,
        routeStopCounts,
      };
    });
  }
}
