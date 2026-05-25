import type { BranchId, ISODateString, OrderId } from './common';

export type PickingTaskStatus = 'PENDING' | 'COMPLETED' | 'CANCELLED';

export interface PickingLineSummary {
  id: string;
  orderLineId: string;
  productId: string;
  quantity: number;
  pickedQuantity: number;
}

export interface PickingTaskSummary {
  id: string;
  orderId: OrderId;
  branchId: BranchId;
  status: PickingTaskStatus;
  lineCount: number;
  createdAt: ISODateString;
  updatedAt: ISODateString;
  completedAt: ISODateString | null;
}

export interface ListPickingTasksQuery {
  status?: PickingTaskStatus;
  limit?: number;
  cursor?: string;
}

export interface ListPickingTasksResult {
  items: PickingTaskSummary[];
  nextCursor: string | null;
}

export interface CompletePickingTaskResult {
  pickingTask: PickingTaskSummary;
}
