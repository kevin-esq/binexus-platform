import { z } from 'zod';

export const deliveryConfirmedProofPayload = z.object({
  recipientName: z.string().optional(),
  notes: z.string().optional(),
  photoObjectKey: z.string().optional(),
  signatureObjectKey: z.string().optional(),
  latitude: z.number().optional(),
  longitude: z.number().optional(),
});

export const deliveryConfirmedPayload = z.object({
  deliveryRouteId: z.string(),
  deliveryRouteStopId: z.string(),
  branchId: z.string(),
  orderId: z.string(),
  confirmedBy: z.string(),
  confirmedAt: z.string(),
  proof: deliveryConfirmedProofPayload.optional(),
});

export type DeliveryConfirmedPayload = z.infer<typeof deliveryConfirmedPayload>;
