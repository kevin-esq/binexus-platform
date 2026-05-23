import { z } from 'zod';

export const orderCancelledPayload = z.object({
  orderId: z.string(),
  cancelledBy: z.string(),
  reason: z.string().optional(),
});

export type OrderCancelledPayload = z.infer<typeof orderCancelledPayload>;
