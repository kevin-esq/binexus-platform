import { z } from 'zod';

const saleLinePayload = z.object({
  productId: z.string(),
  productName: z.string(),
  quantity: z.number().int().positive(),
  unitPriceCents: z.number().int().nonnegative(),
  lineTotalCents: z.number().int().nonnegative(),
});

const salePaymentPayload = z.object({
  method: z.enum(['CASH', 'CARD', 'TRANSFER']),
  amountCents: z.number().int().positive(),
});

export const saleCreatedPayload = z.object({
  saleId: z.string(),
  ticketId: z.string(),
  sessionId: z.string(),
  branchId: z.string(),
  terminalId: z.string(),
  cashierId: z.string(),
  customerLabel: z.string(),
  totalCents: z.number().int().nonnegative(),
  currency: z.string().length(3),
  lines: z.array(saleLinePayload).min(1),
  payments: z.array(salePaymentPayload).min(1),
});

export type SaleCreatedPayload = z.infer<typeof saleCreatedPayload>;
