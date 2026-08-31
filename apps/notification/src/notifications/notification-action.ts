import { z } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';

export const NOTIFICATION_ACTION_TYPES = [
  'OPEN_BOOKING_DETAIL',
  'OPEN_CREW_TRIP_BOOKING',
  'OPEN_TRIP_DETAIL',
  'OPEN_TRIP_TRACKING',
  'OPEN_PARCEL_DETAIL',
  'OPEN_PARCEL_APPROVAL',
  'OPEN_WALLET',
  'OPEN_SUBSCRIPTION',
  'OPEN_SHUTTLE_TRACKING',
  'NONE',
] as const;

export type NotificationActionDto =
  | { type: 'OPEN_BOOKING_DETAIL'; params: { bookingId: string } }
  | { type: 'OPEN_CREW_TRIP_BOOKING'; params: { tripId: string; bookingId: string } }
  | { type: 'OPEN_TRIP_DETAIL'; params: { tripId: string } }
  | { type: 'OPEN_TRIP_TRACKING'; params: { tripId: string } }
  | { type: 'OPEN_PARCEL_DETAIL'; params: { parcelId: string } }
  | {
      type: 'OPEN_PARCEL_APPROVAL';
      params: { requestId: string; requestType: 'CUSTODY_EXCEPTION' | 'STOP_DEPARTURE' };
    }
  | { type: 'OPEN_WALLET'; params: Record<string, never> }
  | { type: 'OPEN_SUBSCRIPTION'; params: Record<string, never> }
  | {
      type: 'OPEN_SHUTTLE_TRACKING';
      params: { shuttleTripId: string; bookingId?: string; pickupOrder?: number };
    }
  | { type: 'NONE'; params: Record<string, never> };

const ActionDataSchema = z
  .object({
    bookingId: z.string().uuid().nullish(),
    tripId: z.string().uuid().nullish(),
    parcelId: z.string().uuid().nullish(),
    requestId: z.string().uuid().nullish(),
    requestType: z.enum(['CUSTODY_EXCEPTION', 'STOP_DEPARTURE']).nullish(),
    shuttleTripId: z.string().uuid().nullish(),
    pickupOrder: z.number().int().positive().nullish(),
    deepLink: z.string().trim().min(1).nullish(),
  })
  .passthrough();

const BOOKING_DETAIL_TYPES = new Set<NotificationType>([
  NotificationType.BOOKING_CONFIRMED,
  NotificationType.BOOKING_CANCELLED,
  NotificationType.BOOKING_DISRUPTED,
  NotificationType.PASSENGER_NO_SHOW,
]);

const TRIP_TRACKING_TYPES = new Set<NotificationType>([
  NotificationType.TRIP_BOARDING_REMINDER,
  NotificationType.TRIP_VEHICLE_APPROACHING,
  NotificationType.TRIP_DELAYED,
  NotificationType.TRIP_DELAYED_ALERT,
  NotificationType.OFF_ROUTE_ALERT,
]);

const TRIP_DETAIL_TYPES = new Set<NotificationType>([
  NotificationType.TRIP_ROUTE_CHANGED,
  NotificationType.TRIP_SCHEDULE_CHANGED,
  NotificationType.TRIP_CANCELLED,
  NotificationType.TRIP_DISRUPTED,
  NotificationType.VEHICLE_SUBSTITUTED,
  NotificationType.VEHICLE_SWAPPED,
  NotificationType.INCIDENT_REPORTED,
  NotificationType.CARGO_NEAR_FULL_ALERT,
  NotificationType.TRIP_ASSIGNED,
  NotificationType.DRIVER_SCHEDULE_EDITED,
  NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING,
]);

const NONE_ACTION: NotificationActionDto = { type: 'NONE', params: {} };

export function resolveNotificationAction(
  type: NotificationType,
  rawData: unknown,
): NotificationActionDto {
  if (
    type === NotificationType.WALLET_CREDITED ||
    type === NotificationType.WALLET_DEBITED ||
    type === NotificationType.BOOKING_REFUNDED
  ) {
    return { type: 'OPEN_WALLET', params: {} };
  }
  if (type.startsWith('SUBSCRIPTION_')) {
    return { type: 'OPEN_SUBSCRIPTION', params: {} };
  }

  const parsed = ActionDataSchema.safeParse(rawData);
  if (!parsed.success) return NONE_ACTION;
  const data = parsed.data;

  if (type === NotificationType.BOOKING_CREATED) {
    return data.tripId && data.bookingId
      ? {
          type: 'OPEN_CREW_TRIP_BOOKING',
          params: { tripId: data.tripId, bookingId: data.bookingId },
        }
      : NONE_ACTION;
  }

  if (
    type === NotificationType.BOOKING_CANCELLED &&
    data.tripId &&
    data.bookingId &&
    data.deepLink === `vietride://driver/trips/${data.tripId}/bookings/${data.bookingId}`
  ) {
    return {
      type: 'OPEN_CREW_TRIP_BOOKING',
      params: { tripId: data.tripId, bookingId: data.bookingId },
    };
  }

  if (BOOKING_DETAIL_TYPES.has(type)) {
    return data.bookingId
      ? { type: 'OPEN_BOOKING_DETAIL', params: { bookingId: data.bookingId } }
      : NONE_ACTION;
  }

  if (type === NotificationType.STOP_DISABLED) {
    if (data.bookingId) {
      return { type: 'OPEN_BOOKING_DETAIL', params: { bookingId: data.bookingId } };
    }
    return data.tripId
      ? { type: 'OPEN_TRIP_DETAIL', params: { tripId: data.tripId } }
      : NONE_ACTION;
  }

  if (TRIP_TRACKING_TYPES.has(type)) {
    return data.tripId
      ? { type: 'OPEN_TRIP_TRACKING', params: { tripId: data.tripId } }
      : NONE_ACTION;
  }

  if (TRIP_DETAIL_TYPES.has(type)) {
    return data.tripId
      ? { type: 'OPEN_TRIP_DETAIL', params: { tripId: data.tripId } }
      : NONE_ACTION;
  }

  if (type.startsWith('PARCEL_')) {
    if (type === NotificationType.PARCEL_APPROVAL_REQUESTED) {
      return data.requestId && data.requestType
        ? {
            type: 'OPEN_PARCEL_APPROVAL',
            params: { requestId: data.requestId, requestType: data.requestType },
          }
        : NONE_ACTION;
    }
    return data.parcelId
      ? { type: 'OPEN_PARCEL_DETAIL', params: { parcelId: data.parcelId } }
      : NONE_ACTION;
  }

  if (type.startsWith('SHUTTLE_')) {
    if (data.shuttleTripId) {
      return {
        type: 'OPEN_SHUTTLE_TRACKING',
        params: {
          shuttleTripId: data.shuttleTripId,
          ...(data.bookingId ? { bookingId: data.bookingId } : {}),
          ...(data.pickupOrder ? { pickupOrder: data.pickupOrder } : {}),
        },
      };
    }
    if (type === NotificationType.SHUTTLE_UNFULFILLED && data.bookingId) {
      return { type: 'OPEN_BOOKING_DETAIL', params: { bookingId: data.bookingId } };
    }
  }

  return NONE_ACTION;
}
