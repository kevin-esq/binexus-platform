import { z } from 'zod';

export const orderSettledPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  settledBy: z.string(),
  settledAt: z.string(),
  reason: z.string().optional(),
});

export type OrderSettledPayload = z.infer<typeof orderSettledPayload>;
