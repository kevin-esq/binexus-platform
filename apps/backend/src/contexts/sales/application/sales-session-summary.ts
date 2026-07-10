import {
  type BranchId,
  type SalesSessionSummary,
  type TicketSummary,
  type UserId,
} from '@binexus/types';
import {
  type PaymentCapture,
  type SalesSession,
  type Ticket,
  type TicketLine,
} from '@prisma/client';

export function toSalesSessionSummary(session: SalesSession): SalesSessionSummary {
  return {
    id: session.id,
    branchId: session.branchId as BranchId,
    terminalId: session.terminalId,
    status: session.status as SalesSessionSummary['status'],
    openingFloatCents: session.openingFloatCents,
    currency: session.currency,
    openedByUserId: session.openedByUserId as UserId,
    openedAt: session.openedAt.toISOString(),
    closedByUserId: (session.closedByUserId as UserId | null) ?? null,
    closedAt: session.closedAt?.toISOString() ?? null,
    expectedClosingCents: session.expectedClosingCents,
    declaredClosingCents: session.declaredClosingCents,
    discrepancyCents: session.discrepancyCents,
    discrepancyReason: session.discrepancyReason,
    closeNotes: session.closeNotes,
  };
}

export function toTicketSummary(
  ticket: Ticket & { lines: TicketLine[]; paymentCaptures: PaymentCapture[] },
): TicketSummary {
  return {
    id: ticket.id,
    sessionId: ticket.sessionId,
    branchId: ticket.branchId as BranchId,
    terminalId: ticket.terminalId,
    customerLabel: ticket.customerLabel,
    status: ticket.status as TicketSummary['status'],
    totalCents: ticket.totalCents,
    currency: ticket.currency,
    cashierUserId: ticket.cashierUserId as UserId,
    createdAt: ticket.createdAt.toISOString(),
    lines: ticket.lines.map((line) => ({
      productId: line.productId,
      productName: line.productName,
      quantity: line.quantity,
      unitPriceCents: line.unitPriceCents,
      lineTotalCents: line.lineTotalCents,
    })),
    paymentCaptures: ticket.paymentCaptures.map((capture) => ({
      id: capture.id,
      method: capture.method as TicketSummary['paymentCaptures'][number]['method'],
      amountCents: capture.amountCents,
      currency: capture.currency,
      capturedAt: capture.capturedAt.toISOString(),
    })),
  };
}

export type SalesSessionWithTickets = SalesSession & {
  tickets?: Array<Ticket & { lines: TicketLine[]; paymentCaptures?: PaymentCapture[] }>;
};
