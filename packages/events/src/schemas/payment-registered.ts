import { z } from 'zod';

export const paymentRegisteredPayload = z.object({
  paymentId: z.string(),
  orderId: z.string().optional(),
  saleId: z.string().optional(),
  amountCents: z.number().int().positive(),
  currency: z.string().length(3),
  method: z.enum(['CASH', 'CARD', 'TRANSFER', 'CREDIT']),
});

export type PaymentRegisteredPayload = z.infer<typeof paymentRegisteredPayload>;
