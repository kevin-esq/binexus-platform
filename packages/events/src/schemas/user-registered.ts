import { z } from 'zod';

export const userRegisteredPayload = z.object({
  userId: z.string(),
  email: z.string().email(),
  role: z.string(),
  branchId: z.string().nullable(),
});

export type UserRegisteredPayload = z.infer<typeof userRegisteredPayload>;
