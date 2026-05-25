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
