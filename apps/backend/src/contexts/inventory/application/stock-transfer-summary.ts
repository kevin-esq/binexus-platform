import { type StockTransferSummary } from '@binexus/types';
import type { StockTransfer } from '@prisma/client';

export function toStockTransferSummary(row: StockTransfer): StockTransferSummary {
  return {
    id: row.id,
    sourceBranchId: row.sourceBranchId as StockTransferSummary['sourceBranchId'],
    destinationBranchId: row.destinationBranchId as StockTransferSummary['destinationBranchId'],
    productId: row.productId,
    quantity: row.quantity,
    status: row.status,
    reason: row.reason,
    createdAt: row.createdAt.toISOString(),
    updatedAt: row.updatedAt.toISOString(),
    receivedAt: row.receivedAt?.toISOString() ?? null,
    cancelledAt: row.cancelledAt?.toISOString() ?? null,
  };
}
