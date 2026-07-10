import type { BranchId, ISODateString, UserId } from './common';
import type { PaymentMethod } from './payments';

export type SalesSessionStatus = 'OPEN' | 'CLOSED';

export type TicketStatus = 'COMPLETED';

export const WALK_IN_CUSTOMER_LABEL = 'walk-in';

export interface SalesSessionSummary {
  id: string;
  branchId: BranchId;
  terminalId: string;
  status: SalesSessionStatus;
  openingFloatCents: number;
  currency: string;
  openedByUserId: UserId;
  openedAt: ISODateString;
  closedByUserId: UserId | null;
  closedAt: ISODateString | null;
  expectedClosingCents: number | null;
  declaredClosingCents: number | null;
  discrepancyCents: number | null;
  discrepancyReason: string | null;
  closeNotes: string | null;
}

export interface TicketLineSummary {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
  lineTotalCents: number;
}

export interface TicketSummary {
  id: string;
  sessionId: string;
  branchId: BranchId;
  terminalId: string;
  customerLabel: string;
  status: TicketStatus;
  totalCents: number;
  currency: string;
  cashierUserId: UserId;
  createdAt: ISODateString;
  lines: TicketLineSummary[];
  paymentCaptures: PaymentCaptureSummary[];
}

export interface PaymentCaptureSummary {
  id: string;
  method: PaymentMethod;
  amountCents: number;
  currency: string;
  capturedAt: ISODateString;
}

export interface CreateSalePaymentInput {
  method: PaymentMethod;
  amountCents: number;
}

export interface OpenSalesSessionInput {
  branchId?: BranchId;
  terminalId: string;
  openingFloatCents: number;
  currency?: string;
}

export interface OpenSalesSessionResult {
  session: SalesSessionSummary;
}

export interface GetCurrentSalesSessionQuery {
  terminalId: string;
  branchId?: BranchId;
}

export interface GetCurrentSalesSessionResult {
  session: SalesSessionSummary | null;
}

export interface CreateSaleLineInput {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceCents: number;
}

export interface CreateSaleInput {
  lines: CreateSaleLineInput[];
  currency?: string;
  payments: CreateSalePaymentInput[];
}

export interface CreateSaleResult {
  ticket: TicketSummary;
}

export interface CloseSalesSessionInput {
  declaredClosingCents: number;
  notes?: string;
  discrepancyReason?: string;
}

export interface CloseSalesSessionResult {
  session: SalesSessionSummary;
}
