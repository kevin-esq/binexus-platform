import { DomainEventName } from '@binexus/events';
import { type AssignOrderToDeliveryRouteResult, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import {
  DeliveryRouteCandidateStatus,
  DeliveryRouteStatus,
  DeliveryRouteStopStatus,
} from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export class AssignOrderToDeliveryRouteCommand extends AppCommand<AssignOrderToDeliveryRouteResult> {
  constructor(
    readonly deliveryRouteId: string,
    readonly orderIds: OrderId[],
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.deliveryRouteId.trim()) {
      throw new BadRequestException('deliveryRouteId is required.');
    }
    if (!this.orderIds.length) {
      throw new BadRequestException('orderIds must contain at least one order.');
    }
    const unique = new Set(this.orderIds);
    if (unique.size !== this.orderIds.length) {
      throw new BadRequestException('orderIds must not contain duplicates.');
    }
  }
}

@Injectable()
@CommandHandler(AssignOrderToDeliveryRouteCommand)
export class AssignOrderToDeliveryRouteHandler extends AppCommandHandler<AssignOrderToDeliveryRouteCommand> {
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

  async execute(
    command: AssignOrderToDeliveryRouteCommand,
  ): Promise<AssignOrderToDeliveryRouteResult> {
    const ctx = this.tenantContext.current();

    return this.prisma.$transaction(async (tx) => {
      const route = await tx.deliveryRoute.findFirst({
        where: { id: command.deliveryRouteId, tenantId: ctx.tenantId },
        include: { _count: { select: { stops: true } } },
      });

      if (!route) {
        throw new NotFoundException(`Delivery route ${command.deliveryRouteId} not found`);
      }

      if (route.status !== DeliveryRouteStatus.PLANNED) {
        throw new BadRequestException(
          `Delivery route ${command.deliveryRouteId} is not planned (status=${route.status})`,
        );
      }

      const candidates = await tx.deliveryRouteCandidate.findMany({
        where: {
          tenantId: ctx.tenantId,
          orderId: { in: command.orderIds },
        },
      });

      if (candidates.length !== command.orderIds.length) {
        const found = new Set(candidates.map((c) => c.orderId));
        const missing = command.orderIds.filter((id) => !found.has(id));
        throw new BadRequestException(
          `No delivery route candidate for order(s): ${missing.join(', ')}`,
        );
      }

      for (const candidate of candidates) {
        if (candidate.status !== DeliveryRouteCandidateStatus.READY) {
          throw new BadRequestException(
            `Order ${candidate.orderId} is not ready for assignment (status=${candidate.status})`,
          );
        }
        if (candidate.branchId !== route.branchId) {
          throw new BadRequestException(
            `Order ${candidate.orderId} branch ${candidate.branchId} does not match route branch ${route.branchId}`,
          );
        }
      }

      let sequence = route._count.stops;
      const assignedOrderIds: OrderId[] = [];

      for (const orderId of command.orderIds) {
        sequence += 1;
        await tx.deliveryRouteStop.create({
          data: {
            tenantId: ctx.tenantId,
            deliveryRouteId: route.id,
            orderId,
            sequence,
            status: DeliveryRouteStopStatus.PLANNED,
          },
        });

        await tx.deliveryRouteCandidate.updateMany({
          where: { tenantId: ctx.tenantId, orderId },
          data: {
            status: DeliveryRouteCandidateStatus.ASSIGNED,
            deliveryRouteId: route.id,
          },
        });

        assignedOrderIds.push(orderId as OrderId);
      }

      const event = this.eventBus.build(
        DomainEventName.DELIVERY_ROUTE_ASSIGNED,
        {
          deliveryRouteId: route.id,
          branchId: route.branchId,
          orderIds: assignedOrderIds,
          assignedBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      const stopCount = route._count.stops + assignedOrderIds.length;

      return {
        deliveryRouteId: route.id,
        assignedOrderIds,
        stopCount,
      };
    });
  }
}
