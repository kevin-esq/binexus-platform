import type { BranchId, ISODateString, OrderId, UserId } from './common';

export type DeliveryRouteStatus = 'PLANNED' | 'DISPATCHED' | 'COMPLETED' | 'CANCELLED';

export type DeliveryRouteStopStatus = 'PLANNED' | 'DELIVERED' | 'FAILED' | 'SKIPPED';

export type DeliveryRouteCandidateStatus = 'READY' | 'ASSIGNED' | 'CANCELLED';

export interface DeliveryRouteSummary {
  id: string;
  branchId: BranchId;
  status: DeliveryRouteStatus;
  driverUserId: UserId | null;
  plannedDate: ISODateString | null;
  dispatchedAt: ISODateString | null;
  stopCount: number;
  createdAt: ISODateString;
  updatedAt: ISODateString;
}

export interface DeliveryRouteCandidateSummary {
  id: string;
  orderId: OrderId;
  branchId: BranchId;
  status: DeliveryRouteCandidateStatus;
  deliveryRouteId: string | null;
  createdAt: ISODateString;
  updatedAt: ISODateString;
}

export interface ListDeliveryRoutesQuery {
  status?: DeliveryRouteStatus;
  branchId?: BranchId;
  limit?: number;
  cursor?: string;
}

export interface ListDeliveryRoutesResult {
  items: DeliveryRouteSummary[];
  nextCursor: string | null;
}

export interface ListDeliveryRouteCandidatesQuery {
  status?: DeliveryRouteCandidateStatus;
  branchId?: BranchId;
  limit?: number;
  cursor?: string;
}

export interface ListDeliveryRouteCandidatesResult {
  items: DeliveryRouteCandidateSummary[];
  nextCursor: string | null;
}

export interface CreateDeliveryRouteInput {
  branchId: BranchId;
  driverUserId?: UserId;
  plannedDate?: string;
}

export interface CreateDeliveryRouteResult {
  deliveryRoute: DeliveryRouteSummary;
}

export interface AssignOrderToDeliveryRouteInput {
  orderIds: OrderId[];
}

export interface AssignOrderToDeliveryRouteResult {
  deliveryRouteId: string;
  assignedOrderIds: OrderId[];
  stopCount: number;
}

export interface DispatchDeliveryRouteInput {
  driverUserId?: UserId;
}

export interface DispatchDeliveryRouteResult {
  deliveryRouteId: string;
  status: 'DISPATCHED';
  driverUserId: UserId;
  dispatchedAt: ISODateString;
  orderIds: OrderId[];
}
