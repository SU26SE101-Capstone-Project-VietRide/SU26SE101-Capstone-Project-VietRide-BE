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
      `${transfer.originalSeatNumber ?? 'chưa xác định'} -> ${transfer.newSeatNumber ?? 'đang chờ xếp ghế'}`,
  );

  return {
    userId: payload.recipientUserId,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Xe thay thế đã được sắp xếp',
    body:
      `Xe ${payload.newVehiclePlateNumber} khởi hành lúc ${payload.newTripDepartureDateTime}. ` +
      `Cập nhật ghế: ${seatChanges.join('; ')}.`,
    data: bookingTransferredData(payload),
  };
}

function mapRouteChangeAutoFallback(
  payload: BookingRouteChangeAutoFallbackAppliedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_ROUTE_CHANGED,
    title: 'Đã tự động chuyển điểm đến',
    body:
      `Vì bạn chưa phản hồi, xe sẽ đưa bạn đến bến ${payload.fallbackDestinationStationId}; ` +
      'nhà xe sẽ bố trí xe trung chuyển đưa bạn về điểm dừng ban đầu.',
    data: bookingEventData(payload),
  };
}

function mapPendingActionAutoResolved(
  payload: BookingPendingActionAutoResolvedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Đã chấp nhận lịch chuyến mới',
    body: `Lịch chuyến ${payload.tripId} đã được tự động chấp nhận.`,
    data: bookingEventData(payload),
  };
}

function mapSeatReassignmentRequired(
  payload: BookingSeatReassignmentRequiredEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Cần chọn lại ghế',
    body: `Ghế ${payload.seatNumbers.join(', ')} của bạn trên chuyến ${payload.tripId} cần được chọn lại.`,
    data: bookingEventData(payload),
  };
}

function mapScheduleChangeInformational(
  payload: BookingScheduleChangeInformationalEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Lịch chuyến xe đã thay đổi',
    body: `Giờ khởi hành chuyến ${payload.tripId} đã thay đổi. Vui lòng kiểm tra lịch mới.`,
    data: bookingEventData(payload),
  };
}

function mapScheduleChangeRequired(
  payload: BookingScheduleChangeRequiredEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Cần xác nhận lịch chuyến mới',
    body: `Lịch chuyến ${payload.tripId} đã thay đổi. Vui lòng phản hồi trước ${payload.deadline}.`,
    data: bookingEventData(payload),
  };
}

function mapPendingActionRealerted(payload: BookingPendingActionRealertedEvent): CreateNotificationDto {
  if (payload.reason === 'PENDING_SEAT_ASSIGNMENT') {
    return {
      userId: payload.userId,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: 'Nhắc lại: cần chọn lại ghế',
      body: `Bạn vẫn cần chọn lại ghế ${payload.seatNumbers.join(', ')} trước ${payload.deadline}.`,
      data: bookingEventData(payload),
    };
  }

  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Nhắc lại: cần xác nhận lịch chuyến mới',
    body: `Bạn vẫn cần phản hồi về lịch chuyến ${payload.tripId} trước ${payload.deadline}.`,
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
