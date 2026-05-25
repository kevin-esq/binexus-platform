import { z } from 'zod';

export const orderDeliveredPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  deliveredBy: z.string(),
  deliveredAt: z.string(),
});

export type OrderDeliveredPayload = z.infer<typeof orderDeliveredPayload>;
