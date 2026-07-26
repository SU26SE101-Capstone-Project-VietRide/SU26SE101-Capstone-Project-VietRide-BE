import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BOOKING_TRANSFERRED_ROUTING_KEY,
  BookingPendingActionAutoResolvedEventSchema,
  BookingPendingActionRealertedEventSchema,
  BookingRouteChangeAutoFallbackAppliedEventSchema,
  BookingScheduleChangeInformationalEventSchema,
  BookingScheduleChangeRequiredEventSchema,
  BookingSeatReassignmentRequiredEventSchema,
  BookingTransferredEventSchema,
  type BookingPendingActionAutoResolvedEvent,
  type BookingPendingActionRealertedEvent,
  type BookingRouteChangeAutoFallbackAppliedEvent,
  type BookingScheduleChangeInformationalEvent,
  type BookingScheduleChangeRequiredEvent,
  type BookingSeatReassignmentRequiredEvent,
  type BookingTransferredEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';

export type BookingTripChangeRoutingKey =
  | typeof BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY
  | typeof BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY
  | typeof BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY
  | typeof BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY
  | typeof BOOKING_TRANSFERRED_ROUTING_KEY;

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
    case BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY:
      return mapPendingActionAutoResolved(
        BookingPendingActionAutoResolvedEventSchema.parse(payload),
      );
    case BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY:
      return mapRouteChangeAutoFallback(
        BookingRouteChangeAutoFallbackAppliedEventSchema.parse(payload),
      );
    case BOOKING_TRANSFERRED_ROUTING_KEY: {
      const transferred = BookingTransferredEventSchema.parse(payload);
      return mapBookingTransferred(transferred);
    }
  }
}

function mapBookingTransferred(payload: BookingTransferredEvent): CreateNotificationDto {
  const seatChanges = payload.transfers.map(
    (transfer) =>
      `${transfer.originalSeatNumber ?? 'chua xac dinh'} -> ${transfer.newSeatNumber ?? 'dang cho xep ghe'}`,
  );

  return {
    userId: payload.recipientUserId,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Xe thay the da duoc sap xep',
    body:
      `Xe ${payload.newVehiclePlateNumber} khoi hanh luc ${payload.newTripDepartureDateTime}. ` +
      `Cap nhat ghe: ${seatChanges.join('; ')}.`,
    data: bookingTransferredData(payload),
  };
}

function mapRouteChangeAutoFallback(
  payload: BookingRouteChangeAutoFallbackAppliedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_ROUTE_CHANGED,
    title: 'Da tu dong chuyen diem den',
    body:
      `Vi ban chua phan hoi, xe se dua ban den terminal ${payload.fallbackDestinationStationId}; ` +
      'nha xe se bo tri shuttle dua ban ve diem dung ban dau.',
    data: bookingEventData(payload),
  };
}

function mapPendingActionAutoResolved(
  payload: BookingPendingActionAutoResolvedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Da chap nhan lich chuyen moi',
    body: `Lich chuyen ${payload.tripId} da duoc tu dong chap nhan.`,
    data: bookingEventData(payload),
  };
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
  const { userId, ...data } = payload;
  void userId;
  return data;
}

function bookingTransferredData(
  payload: BookingTransferredEvent,
): Omit<BookingTransferredEvent, 'recipientUserId' | 'notifyPassengers'> {
  const { recipientUserId, notifyPassengers, ...data } = payload;
  void recipientUserId;
  void notifyPassengers;
  return data;
}
