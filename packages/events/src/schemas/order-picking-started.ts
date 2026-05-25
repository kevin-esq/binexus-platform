import { z } from 'zod';

export const orderPickingStartedPayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  lineCount: z.number().int().nonnegative(),
});

export type OrderPickingStartedPayload = z.infer<typeof orderPickingStartedPayload>;
