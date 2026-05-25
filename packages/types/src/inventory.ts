import type { BranchId, ISODateString } from './common';

export interface StockItemSummary {
  id: string;
  branchId: BranchId;
  productId: string;
  onHand: number;
  reserved: number;
  available: number;
  createdAt: ISODateString;
  updatedAt: ISODateString;
}

export interface ListStockItemsQuery {
  branchId?: string;
  productId?: string;
  limit?: number;
  cursor?: string;
}

export interface ListStockItemsResult {
  items: StockItemSummary[];
  nextCursor: string | null;
}

export interface AdjustStockInput {
  branchId: BranchId;
  productId: string;
  delta: number;
  reason: string;
}

export interface AdjustStockResult {
  stockItem: StockItemSummary;
  movementId: string;
}

export type StockTransferStatus = 'PENDING' | 'RECEIVED' | 'CANCELLED';

export interface StockTransferSummary {
  id: string;
  sourceBranchId: BranchId;
  destinationBranchId: BranchId;
  productId: string;
  quantity: number;
  status: StockTransferStatus;
  reason: string | null;
  createdAt: ISODateString;
  updatedAt: ISODateString;
  receivedAt: ISODateString | null;
  cancelledAt: ISODateString | null;
}

export interface CreateStockTransferInput {
  sourceBranchId: BranchId;
  destinationBranchId: BranchId;
  productId: string;
  quantity: number;
  reason?: string;
}

export interface CreateStockTransferResult {
  transfer: StockTransferSummary;
}

export interface ListStockTransfersQuery {
  status?: StockTransferStatus;
  limit?: number;
  cursor?: string;
}

export interface ListStockTransfersResult {
  items: StockTransferSummary[];
  nextCursor: string | null;
}

export interface ReceiveStockTransferResult {
  transfer: StockTransferSummary;
  sourceMovementId: string;
  destinationMovementId: string;
}

export interface CancelStockTransferResult {
  transfer: StockTransferSummary;
}
