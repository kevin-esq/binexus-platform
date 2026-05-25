import { DomainEventName } from '@binexus/events';
import { type BranchId, type CreateDeliveryRouteResult, type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { DeliveryRouteStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toDeliveryRouteSummary } from '../delivery-route-summary';

export class CreateDeliveryRouteCommand extends AppCommand<CreateDeliveryRouteResult> {
  constructor(
    readonly branchId: BranchId,
    readonly issuedBy: UserId,
    readonly driverUserId?: UserId,
    readonly plannedDate?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.branchId.trim()) {
      throw new BadRequestException('branchId is required.');
    }
    if (this.plannedDate !== undefined && this.plannedDate.trim().length > 0) {
      const parsed = Date.parse(this.plannedDate);
      if (Number.isNaN(parsed)) {
        throw new BadRequestException('plannedDate must be a valid ISO date string.');
      }
    }
  }
}

@Injectable()
@CommandHandler(CreateDeliveryRouteCommand)
export class CreateDeliveryRouteHandler extends AppCommandHandler<CreateDeliveryRouteCommand> {
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

  async execute(command: CreateDeliveryRouteCommand): Promise<CreateDeliveryRouteResult> {
    const ctx = this.tenantContext.current();
    const plannedDate = command.plannedDate?.trim().length ? new Date(command.plannedDate) : null;

    return this.prisma.$transaction(async (tx) => {
      const route = await tx.deliveryRoute.create({
        data: {
          tenantId: ctx.tenantId,
          branchId: command.branchId,
          status: DeliveryRouteStatus.PLANNED,
          driverUserId: command.driverUserId ?? null,
          plannedDate,
          createdByUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.DELIVERY_ROUTE_CREATED,
        {
          deliveryRouteId: route.id,
          branchId: route.branchId,
          driverUserId: route.driverUserId ?? undefined,
          plannedDate: route.plannedDate?.toISOString(),
          createdBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return { deliveryRoute: toDeliveryRouteSummary(route, 0) };
    });
  }
}
