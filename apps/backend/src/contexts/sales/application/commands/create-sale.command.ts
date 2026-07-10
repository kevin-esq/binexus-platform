import { DomainEventName } from '@binexus/events';
import {
  type CreateSaleInput,
  type CreateSaleResult,
  type PosWalkInPaymentMethod,
  WALK_IN_CUSTOMER_LABEL,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import {
  type PaymentMethod,
  SalesSessionStatus,
  StockMovementType,
  TicketStatus,
} from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toTicketSummary } from '../sales-session-summary';
import { validateSalePayments } from '../validate-sale-payments';

export class CreateSaleCommand extends AppCommand<CreateSaleResult> {
  constructor(
    readonly sessionId: string,
    readonly input: CreateSaleInput,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.sessionId.trim()) {
      throw new BadRequestException('sessionId is required.');
    }
    if (!this.input.lines?.length) {
      throw new BadRequestException('At least one line is required.');
    }
    if (!this.input.payments?.length) {
      throw new BadRequestException('payments must include at least one capture.');
    }
    for (const line of this.input.lines) {
      if (!line.productId.trim() || !line.productName.trim()) {
        throw new BadRequestException('Each line requires productId and productName.');
      }
      if (!Number.isInteger(line.quantity) || line.quantity < 1) {
        throw new BadRequestException('quantity must be a positive integer.');
      }
      if (!Number.isInteger(line.unitPriceCents) || line.unitPriceCents < 0) {
        throw new BadRequestException('unitPriceCents must be a non-negative integer.');
      }
    }
  }
}

@Injectable()
@CommandHandler(CreateSaleCommand)
export class CreateSaleHandler extends AppCommandHandler<CreateSaleCommand> {
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

  async execute(command: CreateSaleCommand): Promise<CreateSaleResult> {
    const ctx = this.tenantContext.current();
    const currency = command.input.currency ?? 'MXN';

    if (!/^[A-Z]{3}$/.test(currency)) {
      throw new BadRequestException('currency must be a 3-letter ISO code.');
    }

    const lineSnapshots = command.input.lines.map((line) => ({
      productId: line.productId.trim(),
      productName: line.productName.trim(),
      quantity: line.quantity,
      unitPriceCents: line.unitPriceCents,
      lineTotalCents: line.quantity * line.unitPriceCents,
    }));

    const totalCents = lineSnapshots.reduce((sum, line) => sum + line.lineTotalCents, 0);
    validateSalePayments(command.input.payments, totalCents);

    const paymentSnapshots = command.input.payments.map((payment) => ({
      method: payment.method as PosWalkInPaymentMethod,
      amountCents: payment.amountCents,
    }));

    return this.prisma.$transaction(async (tx) => {
      const session = await tx.salesSession.findFirst({
        where: { id: command.sessionId, tenantId: ctx.tenantId },
      });

      if (!session) {
        throw new NotFoundException(`Sales session ${command.sessionId} not found`);
      }

      if (session.status !== SalesSessionStatus.OPEN) {
        throw new BadRequestException('Sales session must be OPEN to create a sale.');
      }

      if (session.currency !== currency) {
        throw new BadRequestException('Sale currency must match the session currency.');
      }

      for (const line of lineSnapshots) {
        const stock = await tx.stockItem.findFirst({
          where: {
            tenantId: ctx.tenantId,
            branchId: session.branchId,
            productId: line.productId,
          },
        });

        const available = (stock?.onHand ?? 0) - (stock?.reserved ?? 0);
        if (available < line.quantity) {
          throw new BadRequestException(
            `Insufficient stock for product ${line.productId} at branch ${session.branchId}.`,
          );
        }
      }

      const ticket = await tx.ticket.create({
        data: {
          tenantId: ctx.tenantId,
          sessionId: session.id,
          branchId: session.branchId,
          terminalId: session.terminalId,
          customerLabel: WALK_IN_CUSTOMER_LABEL,
          status: TicketStatus.COMPLETED,
          totalCents,
          currency,
          cashierUserId: command.issuedBy,
        },
      });

      await tx.ticketLine.createMany({
        data: lineSnapshots.map((line) => ({
          tenantId: ctx.tenantId,
          ticketId: ticket.id,
          productId: line.productId,
          productName: line.productName,
          quantity: line.quantity,
          unitPriceCents: line.unitPriceCents,
          lineTotalCents: line.lineTotalCents,
        })),
      });

      const createdPayments = [];
      for (const payment of paymentSnapshots) {
        const capture = await tx.paymentCapture.create({
          data: {
            tenantId: ctx.tenantId,
            ticketId: ticket.id,
            sessionId: session.id,
            method: payment.method as PaymentMethod,
            amountCents: payment.amountCents,
            currency,
          },
        });
        createdPayments.push(capture);
      }

      for (const line of lineSnapshots) {
        const stock = await tx.stockItem.update({
          where: {
            tenantId_branchId_productId: {
              tenantId: ctx.tenantId,
              branchId: session.branchId,
              productId: line.productId,
            },
          },
          data: { onHand: { decrement: line.quantity } },
        });

        await tx.stockMovement.create({
          data: {
            tenantId: ctx.tenantId,
            branchId: session.branchId,
            productId: line.productId,
            type: StockMovementType.SALE,
            quantity: -line.quantity,
            correlationId: command.correlationId ?? null,
            causationId: command.causationId ?? command.commandId,
          },
        });

        if (stock.onHand < 0) {
          throw new BadRequestException(
            `Insufficient stock for product ${line.productId} at branch ${session.branchId}.`,
          );
        }
      }

      const ticketWithRelations = await tx.ticket.findFirstOrThrow({
        where: { id: ticket.id, tenantId: ctx.tenantId },
        include: { lines: true, paymentCaptures: true },
      });

      const saleEvent = this.eventBus.build(
        DomainEventName.SALE_CREATED,
        {
          saleId: ticket.id,
          ticketId: ticket.id,
          sessionId: session.id,
          branchId: session.branchId,
          terminalId: session.terminalId,
          cashierId: command.issuedBy,
          customerLabel: WALK_IN_CUSTOMER_LABEL,
          totalCents,
          currency,
          lines: lineSnapshots,
          payments: paymentSnapshots.map((payment) => ({
            method: payment.method,
            amountCents: payment.amountCents,
          })),
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(saleEvent, tx);

      for (const capture of createdPayments) {
        const paymentEvent = this.eventBus.build(
          DomainEventName.PAYMENT_REGISTERED,
          {
            paymentId: capture.id,
            saleId: ticket.id,
            amountCents: capture.amountCents,
            currency: capture.currency,
            method: capture.method,
          },
          {
            correlationId: command.correlationId,
            causationId: command.causationId ?? command.commandId,
          },
        );
        await this.outbox.record(paymentEvent, tx);
      }

      return { ticket: toTicketSummary(ticketWithRelations) };
    });
  }
}
