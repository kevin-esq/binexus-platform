import { z } from 'zod';

export const deliveryRouteAssignedPayload = z.object({
  deliveryRouteId: z.string(),
  branchId: z.string(),
  orderIds: z.array(z.string()).min(1),
  assignedBy: z.string(),
});

export type DeliveryRouteAssignedPayload = z.infer<typeof deliveryRouteAssignedPayload>;
