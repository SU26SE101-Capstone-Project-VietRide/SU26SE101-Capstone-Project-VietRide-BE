import { z } from 'zod';

const eventIdentityFields = {
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
};

const parcelDeliveredPendingConfirmEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    tripId: z.string().uuid(),
    userId: z.string().uuid().optional(),
    recipientUserIds: z.array(z.string().uuid()).min(1).optional(),
    expiresAt: z.string().datetime({ offset: true }).optional(),
  })
  .strict();
export type ParcelDeliveredPendingConfirmEvent = z.infer<
  typeof parcelDeliveredPendingConfirmEventSchema
>;

const parcelDeliveryConfirmationRealertedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    tripId: z.string().uuid(),
    expiredAt: z.string().datetime({ offset: true }),
  })
  .strict();
export type ParcelDeliveryConfirmationRealertedEvent = z.infer<
  typeof parcelDeliveryConfirmationRealertedEventSchema
>;

const parcelPendingOperatorActionRealertedEventSchema = z
  .object({
    ...eventIdentityFields,
    parcelId: z.string().uuid(),
    parcelCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    userId: z.string().uuid(),
    tripId: z.string().uuid(),
  })
  .strict();
export type ParcelPendingOperatorActionRealertedEvent = z.infer<
  typeof parcelPendingOperatorActionRealertedEventSchema
>;

export {
  parcelDeliveredPendingConfirmEventSchema as ParcelDeliveredPendingConfirmEventSchema,
  parcelDeliveryConfirmationRealertedEventSchema as ParcelDeliveryConfirmationRealertedEventSchema,
  parcelPendingOperatorActionRealertedEventSchema as ParcelPendingOperatorActionRealertedEventSchema,
};

export const PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY =
  'parcel.parcel.delivered_pending_confirm';
export const PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY =
  'parcel.parcel.delivery_confirmation_realerted';
export const PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY =
  'parcel.parcel.pending_operator_action_realerted';
