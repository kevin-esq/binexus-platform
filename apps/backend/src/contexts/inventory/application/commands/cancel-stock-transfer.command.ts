import { type CancelStockTransferResult } from '@binexus/types';
import { type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { StockTransferStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toStockTransferSummary } from '../stock-transfer-summary';

export class CancelStockTransferCommand extends AppCommand<CancelStockTransferResult> {
  constructor(
    readonly transferId: string,
    readonly issuedBy: UserId,
    metadata?: AppCommandMetadata,
  ) {
    super(metadata);
  }

  validate(): void {
    if (!this.transferId.trim()) {
      throw new BadRequestException('transferId is required.');
    }
  }
}

@Injectable()
@CommandHandler(CancelStockTransferCommand)
export class CancelStockTransferHandler extends AppCommandHandler<CancelStockTransferCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(command: CancelStockTransferCommand): Promise<CancelStockTransferResult> {
    const ctx = this.tenantContext.current();
    const now = new Date();

    return this.prisma.$transaction(async (tx) => {
      const transfer = await tx.stockTransfer.findFirst({
        where: { id: command.transferId, tenantId: ctx.tenantId },
      });

      if (!transfer) {
        throw new NotFoundException(`Transfer ${command.transferId} not found.`);
      }
      if (transfer.status !== StockTransferStatus.PENDING) {
        throw new BadRequestException(
          `Transfer ${command.transferId} is not pending (status=${transfer.status}).`,
        );
      }

      const sourceItem = await tx.stockItem.findFirst({
        where: {
          tenantId: ctx.tenantId,
          branchId: transfer.sourceBranchId,
          productId: transfer.productId,
        },
      });

      if (!sourceItem) {
        throw new BadRequestException('Source stock item missing for pending transfer.');
      }
      if (sourceItem.reserved < transfer.quantity) {
        throw new BadRequestException('Source reserved quantity is lower than transfer quantity.');
      }

      await tx.stockItem.update({
        where: { id: sourceItem.id },
        data: { reserved: { decrement: transfer.quantity } },
      });

      const updatedTransfer = await tx.stockTransfer.update({
        where: { id: transfer.id },
        data: {
          status: StockTransferStatus.CANCELLED,
          cancelledByUserId: command.issuedBy,
          cancelledAt: now,
        },
      });

      return { transfer: toStockTransferSummary(updatedTransfer) };
    });
  }
}
