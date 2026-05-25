import { DomainEventName } from '@binexus/events';
import {
  type CancelOrderResult,
  canTransition,
  OrderState as SharedOrderState,
  type OrderId,
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

export class CancelOrderCommand extends AppCommand<CancelOrderResult> {
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
@CommandHandler(CancelOrderCommand)
export class CancelOrderHandler extends AppCommandHandler<CancelOrderCommand> {
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

  async execute(command: CancelOrderCommand): Promise<CancelOrderResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.CANCELLED;
    const reason = command.reason?.trim() || undefined;

    return this.prisma.$transaction(async (tx) => {
      const order = await tx.order.findFirst({
        where: { id: command.orderId, tenantId: ctx.tenantId },
        select: { id: true, state: true },
      });

      if (!order) {
        throw new NotFoundException(`Order ${command.orderId} not found`);
      }

      const fromState = order.state as SharedOrderState;
      if (!canTransition(fromState, targetState)) {
        throw new BadRequestException(
          `Cannot transition order from ${order.state} to ${targetState}`,
        );
      }

      await tx.order.update({
        where: { id: order.id },
        data: { state: OrderState.CANCELLED },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.CANCELLED,
          reason: reason ?? 'Order cancelled',
          byUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.ORDER_CANCELLED,
        {
          orderId: order.id,
          cancelledBy: command.issuedBy,
          reason,
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
