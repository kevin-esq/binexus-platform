import { z } from 'zod';

export const deliveryConfirmedPayload = z.object({
  deliveryRouteId: z.string(),
  deliveryRouteStopId: z.string(),
  branchId: z.string(),
  orderId: z.string(),
  confirmedBy: z.string(),
  confirmedAt: z.string(),
});

export type DeliveryConfirmedPayload = z.infer<typeof deliveryConfirmedPayload>;
