import { DomainEventName } from '@binexus/events';
import {
  canTransition,
  type OrderId,
  type RequeueFailedDeliveryOrderResult,
  OrderState as SharedOrderState,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { OrderState } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export class RequeueFailedDeliveryOrderCommand extends AppCommand<RequeueFailedDeliveryOrderResult> {
  constructor(
    readonly orderId: OrderId,
    readonly issuedBy: UserId,
    readonly reason?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.orderId.trim()) {
      throw new BadRequestException('orderId is required.');
    }
  }
}

@Injectable()
@CommandHandler(RequeueFailedDeliveryOrderCommand)
export class RequeueFailedDeliveryOrderHandler extends AppCommandHandler<RequeueFailedDeliveryOrderCommand> {
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
    command: RequeueFailedDeliveryOrderCommand,
  ): Promise<RequeueFailedDeliveryOrderResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.READY_FOR_DELIVERY_ROUTE;
    const notes = command.reason?.trim();
    const transitionReason = notes
      ? `Requeued after failed delivery — ${notes}`
      : 'Requeued after failed delivery';

    return this.prisma.$transaction(async (tx) => {
      const order = await tx.order.findFirst({
        where: { id: command.orderId, tenantId: ctx.tenantId },
        include: { _count: { select: { lines: true } } },
      });

      if (!order) {
        throw new NotFoundException(`Order ${command.orderId} not found`);
      }

      const fromState = order.state as SharedOrderState;
      if (fromState === targetState) {
        return { id: order.id as OrderId, state: targetState };
      }

      if (!canTransition(fromState, targetState)) {
        throw new BadRequestException(
          `Cannot transition order from ${order.state} to ${targetState}`,
        );
      }

      await tx.order.update({
        where: { id: order.id },
        data: { state: OrderState.READY_FOR_DELIVERY_ROUTE },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.READY_FOR_DELIVERY_ROUTE,
          reason: transitionReason,
          byUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.ORDER_READY_FOR_DELIVERY_ROUTE,
        {
          orderId: order.id,
          branchId: order.branchId,
          readyBy: command.issuedBy,
          lineCount: order._count.lines,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return { id: order.id as OrderId, state: targetState };
    });
  }
}
