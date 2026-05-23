import { z } from 'zod';

export const saleCreatedPayload = z.object({
  saleId: z.string(),
  branchId: z.string(),
  cashierId: z.string(),
  totalCents: z.number().int().nonnegative(),
  currency: z.string().length(3),
});

export type SaleCreatedPayload = z.infer<typeof saleCreatedPayload>;
