import { z } from 'zod';

export const deliveryRouteCreatedPayload = z.object({
  deliveryRouteId: z.string(),
  branchId: z.string(),
  driverUserId: z.string().optional(),
  plannedDate: z.string().optional(),
  createdBy: z.string(),
});

export type DeliveryRouteCreatedPayload = z.infer<typeof deliveryRouteCreatedPayload>;
