import { DomainEventName } from '@binexus/events';
import { type DispatchDeliveryRouteResult, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export class DispatchDeliveryRouteCommand extends AppCommand<DispatchDeliveryRouteResult> {
  constructor(
    readonly deliveryRouteId: string,
    readonly issuedBy: UserId,
    readonly driverUserId?: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteId.trim()) {
      throw new BadRequestException('deliveryRouteId is required.');
    }
  }
}

@Injectable()
@CommandHandler(DispatchDeliveryRouteCommand)
export class DispatchDeliveryRouteHandler extends AppCommandHandler<DispatchDeliveryRouteCommand> {
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

  async execute(command: DispatchDeliveryRouteCommand): Promise<DispatchDeliveryRouteResult> {
    const ctx = this.tenantContext.current();

    return this.prisma.$transaction(async (tx) => {
      const route = await tx.deliveryRoute.findFirst({
        where: { id: command.deliveryRouteId, tenantId: ctx.tenantId },
        include: {
          stops: { orderBy: { sequence: 'asc' } },
        },
      });

      if (!route) {
        throw new NotFoundException(`Delivery route ${command.deliveryRouteId} not found`);
      }

      const orderIds = route.stops.map((s) => s.orderId as OrderId);
      const driverUserId = (command.driverUserId ?? route.driverUserId) as UserId | null;

      if (route.status === DeliveryRouteStatus.DISPATCHED) {
        if (!driverUserId) {
          throw new BadRequestException(
            `Delivery route ${command.deliveryRouteId} is dispatched but has no driver`,
          );
        }
        return {
          deliveryRouteId: route.id,
          status: 'DISPATCHED',
          driverUserId,
          dispatchedAt: route.dispatchedAt!.toISOString(),
          orderIds,
        };
      }

      if (route.status !== DeliveryRouteStatus.PLANNED) {
        throw new BadRequestException(
          `Delivery route ${command.deliveryRouteId} is not planned (status=${route.status})`,
        );
      }

      if (route.stops.length === 0) {
        throw new BadRequestException(
          `Delivery route ${command.deliveryRouteId} cannot be dispatched without stops`,
        );
      }

      if (!driverUserId) {
        throw new BadRequestException(
          `Delivery route ${command.deliveryRouteId} requires a driver before dispatch`,
        );
      }

      const dispatchedAt = new Date();

      const updated = await tx.deliveryRoute.update({
        where: { id: route.id },
        data: {
          status: DeliveryRouteStatus.DISPATCHED,
          driverUserId,
          dispatchedAt,
          dispatchedByUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.DELIVERY_ROUTE_DISPATCHED,
        {
          deliveryRouteId: route.id,
          branchId: route.branchId,
          driverUserId,
          orderIds,
          dispatchedBy: command.issuedBy,
          dispatchedAt: dispatchedAt.toISOString(),
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return {
        deliveryRouteId: updated.id,
        status: 'DISPATCHED',
        driverUserId,
        dispatchedAt: dispatchedAt.toISOString(),
        orderIds,
      };
    });
  }
}
