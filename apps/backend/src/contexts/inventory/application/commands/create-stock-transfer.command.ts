import { type CreateStockTransferResult } from '@binexus/types';
import { type BranchId, type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { StockTransferStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toStockTransferSummary } from '../stock-transfer-summary';

const MIN_REASON_LENGTH = 3;
const MAX_REASON_LENGTH = 200;

export class CreateStockTransferCommand extends AppCommand<CreateStockTransferResult> {
  constructor(
    readonly sourceBranchId: BranchId,
    readonly destinationBranchId: BranchId,
    readonly productId: string,
    readonly quantity: number,
    readonly issuedBy: UserId,
    readonly reason?: string,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.sourceBranchId.trim()) {
      throw new BadRequestException('sourceBranchId is required.');
    }
    if (!this.destinationBranchId.trim()) {
      throw new BadRequestException('destinationBranchId is required.');
    }
    if (this.sourceBranchId === this.destinationBranchId) {
      throw new BadRequestException('source and destination branches must differ.');
    }
    if (!this.productId.trim()) {
      throw new BadRequestException('productId is required.');
    }
    if (!Number.isInteger(this.quantity) || this.quantity <= 0) {
      throw new BadRequestException('quantity must be a positive integer.');
    }
    if (this.reason !== undefined) {
      const trimmed = this.reason.trim();
      if (trimmed.length > 0 && trimmed.length < MIN_REASON_LENGTH) {
        throw new BadRequestException(`reason must be at least ${MIN_REASON_LENGTH} characters.`);
      }
      if (trimmed.length > MAX_REASON_LENGTH) {
        throw new BadRequestException(`reason must be at most ${MAX_REASON_LENGTH} characters.`);
      }
    }
  }
}

@Injectable()
@CommandHandler(CreateStockTransferCommand)
export class CreateStockTransferHandler extends AppCommandHandler<CreateStockTransferCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(command: CreateStockTransferCommand): Promise<CreateStockTransferResult> {
    const ctx = this.tenantContext.current();
    const reason = command.reason?.trim() || null;

    return this.prisma.$transaction(async (tx) => {
      const sourceItem = await tx.stockItem.findFirst({
        where: {
          tenantId: ctx.tenantId,
          branchId: command.sourceBranchId,
          productId: command.productId,
        },
      });

      if (!sourceItem) {
        throw new BadRequestException(
          `No stock item for product ${command.productId} at source branch ${command.sourceBranchId}.`,
        );
      }

      const available = sourceItem.onHand - sourceItem.reserved;
      if (available < command.quantity) {
        throw new BadRequestException(
          `Insufficient available stock at source branch (available=${available}, requested=${command.quantity}).`,
        );
      }

      await tx.stockItem.update({
        where: { id: sourceItem.id },
        data: { reserved: { increment: command.quantity } },
      });

      const transfer = await tx.stockTransfer.create({
        data: {
          tenantId: ctx.tenantId,
          sourceBranchId: command.sourceBranchId,
          destinationBranchId: command.destinationBranchId,
          productId: command.productId,
          quantity: command.quantity,
          status: StockTransferStatus.PENDING,
          reason,
          createdByUserId: command.issuedBy,
        },
      });

      return { transfer: toStockTransferSummary(transfer) };
    });
  }
}
