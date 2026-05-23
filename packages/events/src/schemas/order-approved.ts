import { z } from 'zod';

export const orderApprovedPayload = z.object({
  orderId: z.string(),
  approvedBy: z.string(),
});

export type OrderApprovedPayload = z.infer<typeof orderApprovedPayload>;
