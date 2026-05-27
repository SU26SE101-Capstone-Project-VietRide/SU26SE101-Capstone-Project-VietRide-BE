import { z } from 'zod';

export const UserRegisteredEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  userId: z.string().uuid(),
  email: z.string().email().optional(),
  phone: z.string().min(8).max(20).optional(),
  role: z.enum(['passenger', 'driver', 'operator_admin', 'system_admin']),
});

export type UserRegisteredEvent = z.infer<typeof UserRegisteredEventSchema>;

export const USER_REGISTERED_ROUTING_KEY = 'identity.user.registered';
