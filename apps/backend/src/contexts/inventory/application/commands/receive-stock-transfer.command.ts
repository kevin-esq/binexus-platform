import { type ReceiveStockTransferResult } from '@binexus/types';
import { type UserId } from '@binexus/types';
import { BadRequestException, Inject, Injectable, NotFoundException } from '@nestjs/common';
import { CommandHandler } from '@nestjs/cqrs';
import { StockMovementType, StockTransferStatus } from '@prisma/client';

import { AppCommand, type AppCommandMetadata } from '../../../../common/commands/app-command';
import { AppCommandHandler } from '../../../../common/commands/app-command-handler';
import { PrismaService } from '../../../../common/prisma/prisma.service';
import { TenantContextService } from '../../../../common/tenant/tenant-context.service';
import { toStockTransferSummary } from '../stock-transfer-summary';

export class ReceiveStockTransferCommand extends AppCommand<ReceiveStockTransferResult> {
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
@CommandHandler(ReceiveStockTransferCommand)
export class ReceiveStockTransferHandler extends AppCommandHandler<ReceiveStockTransferCommand> {
  constructor(
    @Inject(PrismaService)
    private readonly prisma: PrismaService,
    @Inject(TenantContextService)
    private readonly tenantContext: TenantContextService,
  ) {
    super();
  }

  async execute(command: ReceiveStockTransferCommand): Promise<ReceiveStockTransferResult> {
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
      if (sourceItem.onHand < transfer.quantity) {
        throw new BadRequestException('Source onHand is lower than transfer quantity.');
      }

      await tx.stockItem.update({
        where: { id: sourceItem.id },
        data: {
          onHand: { decrement: transfer.quantity },
          reserved: { decrement: transfer.quantity },
        },
      });

      const destExisting = await tx.stockItem.findFirst({
        where: {
          tenantId: ctx.tenantId,
          branchId: transfer.destinationBranchId,
          productId: transfer.productId,
        },
      });

      if (destExisting) {
        await tx.stockItem.update({
          where: { id: destExisting.id },
          data: { onHand: { increment: transfer.quantity } },
        });
      } else {
        await tx.stockItem.create({
          data: {
            tenantId: ctx.tenantId,
            branchId: transfer.destinationBranchId,
            productId: transfer.productId,
            onHand: transfer.quantity,
            reserved: 0,
          },
        });
      }

      const correlationId = command.correlationId ?? command.commandId;
      const causationId = command.causationId ?? command.commandId;

      const sourceMovement = await tx.stockMovement.create({
        data: {
          tenantId: ctx.tenantId,
          branchId: transfer.sourceBranchId,
          productId: transfer.productId,
          type: StockMovementType.TRANSFER_OUT,
          quantity: -transfer.quantity,
          correlationId,
          causationId,
        },
      });

      const destinationMovement = await tx.stockMovement.create({
        data: {
          tenantId: ctx.tenantId,
          branchId: transfer.destinationBranchId,
          productId: transfer.productId,
          type: StockMovementType.TRANSFER_IN,
          quantity: transfer.quantity,
          correlationId,
          causationId,
        },
      });

      const updatedTransfer = await tx.stockTransfer.update({
        where: { id: transfer.id },
        data: {
          status: StockTransferStatus.RECEIVED,
          receivedByUserId: command.issuedBy,
          receivedAt: now,
        },
      });

      return {
        transfer: toStockTransferSummary(updatedTransfer),
        sourceMovementId: sourceMovement.id,
        destinationMovementId: destinationMovement.id,
      };
    });
  }
}
