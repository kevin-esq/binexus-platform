import { z } from 'zod';

export const deliveryFailureReason = z.enum([
  'NO_RECIPIENT',
  'WRONG_ADDRESS',
  'REFUSED',
  'DAMAGED',
  'OTHER',
]);

export const deliveryFailedPayload = z.object({
  deliveryRouteId: z.string(),
  deliveryRouteStopId: z.string(),
  branchId: z.string(),
  orderId: z.string(),
  failureReason: deliveryFailureReason,
  failureNotes: z.string().optional(),
  reportedBy: z.string(),
  reportedAt: z.string(),
});

export type DeliveryFailedPayload = z.infer<typeof deliveryFailedPayload>;
