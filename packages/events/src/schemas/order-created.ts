import { z } from 'zod';

export const orderCreatedPayload = z.object({
  orderId: z.string(),
  customerId: z.string(),
  totalCents: z.number().int().nonnegative(),
  currency: z.string().length(3),
  createdBy: z.string(),
});

export type OrderCreatedPayload = z.infer<typeof orderCreatedPayload>;
