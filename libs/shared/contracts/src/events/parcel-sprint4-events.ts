import { z } from 'zod';

const eventIdentityFields = {
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
};

const parcelLoadedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    tripId: z.string().uuid(),
    actualWeightKg: z.number().positive(),
    userIds: z.array(z.string().uuid()).min(1),
  })
  .strict();

export type ParcelLoadedEvent = z.infer<typeof parcelLoadedEventSchema>;

const parcelAutoRejectedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
    refundAmount: z.number().int().nonnegative(),
  })
  .strict();

export type ParcelAutoRejectedEvent = z.infer<typeof parcelAutoRejectedEventSchema>;

export {
  parcelLoadedEventSchema as ParcelLoadedEventSchema,
  parcelAutoRejectedEventSchema as ParcelAutoRejectedEventSchema,
};

export const PARCEL_LOADED_ROUTING_KEY = 'parcel.parcel.loaded';
export const PARCEL_AUTO_REJECTED_ROUTING_KEY = 'parcel.parcel.auto_rejected';
