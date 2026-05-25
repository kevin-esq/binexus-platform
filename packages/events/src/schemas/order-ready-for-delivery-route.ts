import { z } from 'zod';

export const orderReadyForDeliveryRoutePayload = z.object({
  orderId: z.string(),
  branchId: z.string(),
  readyBy: z.string(),
  lineCount: z.number().int().nonnegative().optional(),
});

export type OrderReadyForDeliveryRoutePayload = z.infer<typeof orderReadyForDeliveryRoutePayload>;
