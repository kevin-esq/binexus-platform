import { DomainEventName } from '@binexus/events';
import {
  type CreateSaleInput,
  type CreateSaleResult,
  WALK_IN_CUSTOMER_LABEL,
  type UserId,
} from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { PaymentMethod, SalesSessionStatus, StockMovementType, TicketStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { EventBusService } from '../../../../common/events/event-bus.service';
import { OutboxService } from '../../../../common/events/outbox.service';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toTicketSummary } from '../sales-session-summary';

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

      const payment = await tx.paymentCapture.create({
        data: {
          tenantId: ctx.tenantId,
          ticketId: ticket.id,
          sessionId: session.id,
          method: PaymentMethod.CASH,
          amountCents: totalCents,
          currency,
        },
      });

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

      const ticketWithLines = await tx.ticket.findFirstOrThrow({
        where: { id: ticket.id, tenantId: ctx.tenantId },
        include: { lines: true },
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
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      const paymentEvent = this.eventBus.build(
        DomainEventName.PAYMENT_REGISTERED,
        {
          paymentId: payment.id,
          saleId: ticket.id,
          amountCents: totalCents,
          currency,
          method: PaymentMethod.CASH,
        },
        {
          correlationId: command.correlationId,
          causationId: command.causationId ?? command.commandId,
        },
      );

      await this.outbox.record(saleEvent, tx);
      await this.outbox.record(paymentEvent, tx);

      return { ticket: toTicketSummary(ticketWithLines) };
    });
  }
}
