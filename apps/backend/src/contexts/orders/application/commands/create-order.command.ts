import { DomainEventName } from '@binexus/events';
import {
  isPaymentMethod,
  type BranchId,
  type OrderId,
  type PaymentMethod,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { OrderState, type PaymentMethod as PrismaPaymentMethod } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';

export interface CreateOrderLineInput {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
}

export interface CreateOrderInput {
  customerId: string;
  branchId?: BranchId;
  currency: string;
  paymentMethod: PaymentMethod;
  lines: CreateOrderLineInput[];
}

export class CreateOrderCommand extends AppCommand<OrderId> {
  constructor(
    readonly input: CreateOrderInput,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.input.customerId.trim()) {
      throw new BadRequestException('customerId is required.');
    }

    if (!this.input.paymentMethod || !isPaymentMethod(this.input.paymentMethod)) {
      throw new BadRequestException(
        'paymentMethod is required and must be one of CASH, CARD, TRANSFER, CREDIT.',
      );
    }

    if (!/^[A-Z]{3}$/.test(this.input.currency)) {
      throw new BadRequestException('currency must be an ISO 4217 uppercase code.');
    }

    if (this.input.lines.length === 0) {
      throw new BadRequestException('At least one order line is required.');
    }

    for (const [index, line] of this.input.lines.entries()) {
      if (!line.productId.trim()) {
        throw new BadRequestException(`lines[${index}].productId is required.`);
      }
      if (!line.productName.trim()) {
        throw new BadRequestException(`lines[${index}].productName is required.`);
      }
      if (!Number.isInteger(line.quantity) || line.quantity <= 0) {
        throw new BadRequestException(`lines[${index}].quantity must be a positive integer.`);
      }
      if (!Number.isInteger(line.unitPriceCents) || line.unitPriceCents < 0) {
        throw new BadRequestException(
          `lines[${index}].unitPriceCents must be a non-negative integer.`,
        );
      }
    }
  }
}

@Injectable()
@CommandHandler(CreateOrderCommand)
export class CreateOrderHandler extends AppCommandHandler<CreateOrderCommand> {
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

  async execute(command: CreateOrderCommand): Promise<OrderId> {
    const ctx = this.tenantContext.current();
    const branchId = command.input.branchId ?? (ctx.branchId as BranchId | null);

    if (!branchId) {
      throw new BadRequestException('branchId is required for order creation.');
    }

    const totalCents = command.input.lines.reduce(
      (sum, line) => sum + line.quantity * line.unitPriceCents,
      0,
    );

    return this.prisma.$transaction(async (tx) => {
      const branch = await tx.branch.findFirst({
        where: { id: branchId, tenantId: ctx.tenantId },
        select: { id: true },
      });

      if (!branch) {
        throw new BadRequestException('branchId does not belong to the current tenant.');
      }

      const order = await tx.order.create({
        data: {
          tenantId: ctx.tenantId,
          branchId,
          customerId: command.input.customerId,
          state: OrderState.DRAFT,
          paymentMethod: command.input.paymentMethod as PrismaPaymentMethod,
          totalCents,
          currency: command.input.currency,
          createdByUserId: command.issuedBy,
        },
        select: { id: true },
      });

      await tx.orderLine.createMany({
        data: command.input.lines.map((line) => ({
          tenantId: ctx.tenantId,
          orderId: order.id,
          productId: line.productId,
          productName: line.productName,
          quantity: line.quantity,
          unitPriceCents: line.unitPriceCents,
          lineTotalCents: line.quantity * line.unitPriceCents,
        })),
      });

      await tx.orderTransition.create({
        data: {
          tenantId: ctx.tenantId,
          orderId: order.id,
          fromState: null,
          toState: OrderState.DRAFT,
          reason: 'Order created',
          byUserId: command.issuedBy,
        },
      });

      const event = this.eventBus.build(
        DomainEventName.ORDER_CREATED,
        {
          orderId: order.id,
          customerId: command.input.customerId,
          totalCents,
          currency: command.input.currency,
          createdBy: command.issuedBy,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(event, tx);

      return order.id as OrderId;
    });
  }
}
