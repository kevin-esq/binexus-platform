import { z } from 'zod';

export const deliveryRouteDispatchedPayload = z.object({
  deliveryRouteId: z.string(),
  branchId: z.string(),
  driverUserId: z.string(),
  orderIds: z.array(z.string()).min(1),
  dispatchedBy: z.string(),
  dispatchedAt: z.string(),
});

export type DeliveryRouteDispatchedPayload = z.infer<typeof deliveryRouteDispatchedPayload>;
