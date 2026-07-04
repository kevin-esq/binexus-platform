import { DomainEventName } from '@binexus/events';
import {
  canTransition,
  type MarkOrderDeliveredResult,
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

import { settleOrderInTransaction, shouldAutoSettleOnDelivery } from './settle-order.command';

export class MarkOrderDeliveredCommand extends AppCommand<MarkOrderDeliveredResult> {
  constructor(
    readonly orderId: OrderId,
    readonly issuedBy: UserId,
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
@CommandHandler(MarkOrderDeliveredCommand)
export class MarkOrderDeliveredHandler extends AppCommandHandler<MarkOrderDeliveredCommand> {
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

  async execute(command: MarkOrderDeliveredCommand): Promise<MarkOrderDeliveredResult> {
    const ctx = this.tenantContext.current();
    const targetState = SharedOrderState.DELIVERED;

    return this.prisma.$transaction(async (tx) => {
      const order = await tx.order.findFirst({
        where: { id: command.orderId, tenantId: ctx.tenantId },
        select: { id: true, branchId: true, state: true, paymentMethod: true },
      });

      if (!order) {
        throw new NotFoundException(`Order ${command.orderId} not found`);
      }

      const fromState = order.state as SharedOrderState;
      if (fromState === SharedOrderState.SETTLED) {
        return { id: order.id as OrderId, state: SharedOrderState.SETTLED };
      }
      if (fromState === targetState) {
        return { id: order.id as OrderId, state: targetState };
      }

      if (!canTransition(fromState, targetState)) {
        throw new BadRequestException(
          `Cannot transition order from ${order.state} to ${targetState}`,
        );
      }

      const deliveredAt = new Date();

      await tx.order.update({
        where: { id: order.id },
        data: { state: OrderState.DELIVERED },
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: order.state,
          toState: OrderState.DELIVERED,
          reason: 'Delivery confirmed',
          byUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.ORDER_DELIVERED,
        {
          orderId: order.id,
          branchId: order.branchId,
          deliveredBy: command.issuedBy,
          deliveredAt: deliveredAt.toISOString(),
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      if (shouldAutoSettleOnDelivery(order.paymentMethod)) {
        await settleOrderInTransaction(
          tx,
          {
            tenantId: ctx.tenantId,
            orderId: order.id,
            issuedBy: command.issuedBy,
            reason: 'Prepaid order settled on delivery',
            correlationId: command.correlationId,
            causationId: command.causationId ?? command.commandId,
            commandId: command.commandId,
            fromStateOverride: SharedOrderState.DELIVERED,
          },
          this.eventBus,
          this.outbox,
        );
        return { id: order.id as OrderId, state: SharedOrderState.SETTLED };
      }

      return { id: order.id as OrderId, state: targetState };
    });
  }
}
