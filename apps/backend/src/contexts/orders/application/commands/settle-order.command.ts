import { DomainEventName } from '@binexus/events';
import {
  AUTO_SETTLE_ON_DELIVERY_METHODS,
  canTransition,
  type OrderId,
  OrderState as SharedOrderState,
  type SettleOrderResult,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { OrderState, type Prisma } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

type TxClient = Prisma.TransactionClient;

export class SettleOrderCommand extends AppCommand<SettleOrderResult> {
  constructor(
    readonly orderId: OrderId,
    readonly issuedBy: UserId,
    readonly reason?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }
}

export async function settleOrderInTransaction(
  tx: TxClient,
  params: {
    tenantId: string;
    orderId: string;
    issuedBy: string;
    reason?: string;
    correlationId?: string;
    causationId?: string;
    commandId?: string;
    fromStateOverride?: SharedOrderState;
  },
  eventBus: EventBusService,
  outbox: OutboxService,
): Promise<SettleOrderResult | null> {
  const order = await tx.order.findFirst({
    where: { id: params.orderId, tenantId: params.tenantId },
    select: { id: true, branchId: true, state: true },
  });

  if (!order) {
    throw new NotFoundException(`Order ${params.orderId} not found`);
  }

  const fromState = (params.fromStateOverride ?? order.state) as SharedOrderState;
  const targetState = SharedOrderState.SETTLED;

  if (fromState === targetState) {
    return { id: order.id as OrderId, state: targetState };
  }

  if (!canTransition(fromState, targetState)) {
    throw new BadRequestException(`Cannot transition order from ${fromState} to ${targetState}`);
  }

  const settledAt = new Date();

  await tx.order.update({
    where: { id: order.id },
    data: { state: OrderState.SETTLED },
  });

  await tx.orderTransition.create({
    data: {
      tenantId: params.tenantId,
      orderId: order.id,
      fromState: fromState as OrderState,
      toState: OrderState.SETTLED,
      reason: params.reason ?? 'Order settled',
      byUserId: params.issuedBy,
    },
  });

  const event = eventBus.build(
    DomainEventName.ORDER_SETTLED,
    {
      orderId: order.id,
      branchId: order.branchId,
      settledBy: params.issuedBy,
      settledAt: settledAt.toISOString(),
      reason: params.reason,
    },
    {
      correlationId: params.correlationId,
      causationId: params.causationId ?? params.commandId,
    },
  );

  await outbox.record(event, tx);

  return { id: order.id as OrderId, state: targetState };
}

@Injectable()
@CommandHandler(SettleOrderCommand)
export class SettleOrderHandler extends AppCommandHandler<SettleOrderCommand> {
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

  async execute(command: SettleOrderCommand): Promise<SettleOrderResult> {
    const ctx = this.tenantContext.current();

    const result = await this.prisma.$transaction((tx) =>
      settleOrderInTransaction(
        tx,
        {
          tenantId: ctx.tenantId,
          orderId: command.orderId,
          issuedBy: command.issuedBy,
          reason: command.reason,
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
          commandId: command.commandId,
        },
        this.eventBus,
        this.outbox,
      ),
    );

    if (!result) {
      throw new NotFoundException(`Order ${command.orderId} not found`);
    }

    return result;
  }
}

export function shouldAutoSettleOnDelivery(paymentMethod: string): boolean {
  return (AUTO_SETTLE_ON_DELIVERY_METHODS as readonly string[]).includes(paymentMethod);
}
