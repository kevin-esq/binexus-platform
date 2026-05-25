import { z } from 'zod';

export const pickingCompletedPayload = z.object({
  orderId: z.string(),
  pickingTaskId: z.string(),
  completedBy: z.string(),
});

export type PickingCompletedPayload = z.infer<typeof pickingCompletedPayload>;
