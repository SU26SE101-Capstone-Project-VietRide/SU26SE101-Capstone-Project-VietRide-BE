import {
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BookingPendingActionRealertedEventSchema,
  BookingScheduleChangeInformationalEventSchema,
  BookingScheduleChangeRequiredEventSchema,
  BookingSeatReassignmentRequiredEventSchema,
  type BookingPendingActionRealertedEvent,
  type BookingScheduleChangeInformationalEvent,
  type BookingScheduleChangeRequiredEvent,
  type BookingSeatReassignmentRequiredEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';

export type BookingTripChangeRoutingKey =
  | typeof BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY
  | typeof BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY;

export function mapBookingTripChangeToNotification(
  routingKey: BookingTripChangeRoutingKey,
  payload: unknown,
): CreateNotificationDto {
  switch (routingKey) {
    case BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY:
      return mapSeatReassignmentRequired(BookingSeatReassignmentRequiredEventSchema.parse(payload));
    case BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY:
      return mapScheduleChangeInformational(
        BookingScheduleChangeInformationalEventSchema.parse(payload),
      );
    case BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY:
      return mapScheduleChangeRequired(BookingScheduleChangeRequiredEventSchema.parse(payload));
    case BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY:
      return mapPendingActionRealerted(BookingPendingActionRealertedEventSchema.parse(payload));
  }
}

function mapSeatReassignmentRequired(
  payload: BookingSeatReassignmentRequiredEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Can chon lai ghe',
    body: `Ghe ${payload.seatNumbers.join(', ')} cua ban tren chuyen ${payload.tripId} can duoc chon lai.`,
    data: bookingEventData(payload),
  };
}

function mapScheduleChangeInformational(
  payload: BookingScheduleChangeInformationalEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Lich chuyen xe da thay doi',
    body: `Gio khoi hanh chuyen ${payload.tripId} da thay doi. Vui long kiem tra lich moi.`,
    data: bookingEventData(payload),
  };
}

function mapScheduleChangeRequired(
  payload: BookingScheduleChangeRequiredEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Can xac nhan lich chuyen moi',
    body: `Lich chuyen ${payload.tripId} da thay doi. Vui long phan hoi truoc ${payload.deadline}.`,
    data: bookingEventData(payload),
  };
}

function mapPendingActionRealerted(payload: BookingPendingActionRealertedEvent): CreateNotificationDto {
  if (payload.reason === 'PENDING_SEAT_ASSIGNMENT') {
    return {
      userId: payload.userId,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: 'Nhac lai: can chon lai ghe',
      body: `Ban van can chon lai ghe ${payload.seatNumbers.join(', ')} truoc ${payload.deadline}.`,
      data: bookingEventData(payload),
    };
  }

  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Nhac lai: can xac nhan lich chuyen moi',
    body: `Ban van can phan hoi ve lich chuyen ${payload.tripId} truoc ${payload.deadline}.`,
    data: bookingEventData(payload),
  };
}

function bookingEventData<T extends { userId: string }>(payload: T): Omit<T, 'userId'> {
  const { userId: _userId, ...data } = payload;
  return data;
}
