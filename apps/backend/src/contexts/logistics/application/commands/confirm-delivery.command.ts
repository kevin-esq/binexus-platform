import { DomainEventName } from '@binexus/events';
import {
  type ConfirmDeliveryResult,
  type DeliveryRouteStatus,
  type OrderId,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus as PrismaRouteStatus, DeliveryRouteStopStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export class ConfirmDeliveryCommand extends AppCommand<ConfirmDeliveryResult> {
  constructor(
    readonly deliveryRouteStopId: string,
    readonly issuedBy: UserId,
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
  ) {
    super();
  }

  async execute(command: ConfirmDeliveryCommand): Promise<ConfirmDeliveryResult> {
    const ctx = this.tenantContext.current();

    return this.prisma.$transaction(async (tx) => {
      const stop = await tx.deliveryRouteStop.findFirst({
        where: { id: command.deliveryRouteStopId, tenantId: ctx.tenantId },
        include: { deliveryRoute: true },
      });

      if (!stop) {
        throw new NotFoundException(`Delivery route stop ${command.deliveryRouteStopId} not found`);
      }

      const route = stop.deliveryRoute;

      if (stop.status === DeliveryRouteStopStatus.DELIVERED) {
        return {
          deliveryRouteStopId: stop.id,
          orderId: stop.orderId as OrderId,
          status: 'DELIVERED',
          deliveredAt: stop.deliveredAt!.toISOString(),
          routeStatus: route.status as DeliveryRouteStatus,
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

      const event = this.eventBus.build(
        DomainEventName.DELIVERY_CONFIRMED,
        {
          deliveryRouteId: route.id,
          deliveryRouteStopId: stop.id,
          branchId: route.branchId,
          orderId: stop.orderId,
          confirmedBy: command.issuedBy,
          confirmedAt: deliveredAt.toISOString(),
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return {
        deliveryRouteStopId: stop.id,
        orderId: stop.orderId as OrderId,
        status: 'DELIVERED',
        deliveredAt: deliveredAt.toISOString(),
        routeStatus,
      };
    });
  }
}
