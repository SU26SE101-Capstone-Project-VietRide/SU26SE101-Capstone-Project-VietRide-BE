import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY,
  BOOKING_TRANSFER_ESCALATED_ROUTING_KEY,
  BOOKING_TRANSFERRED_ROUTING_KEY,
  BookingPendingActionAutoResolvedEventSchema,
  BookingPendingActionRealertedEventSchema,
  BookingRouteChangeAutoFallbackAppliedEventSchema,
  BookingScheduleChangeInformationalEventSchema,
  BookingScheduleChangeRequiredEventSchema,
  BookingSeatReassignmentRequiredEventSchema,
  BookingSeatShortageDetectedEventSchema,
  BookingTransferEscalatedEventSchema,
  BookingTransferredEventSchema,
  type BookingPendingActionAutoResolvedEvent,
  type BookingPendingActionRealertedEvent,
  type BookingRouteChangeAutoFallbackAppliedEvent,
  type BookingScheduleChangeInformationalEvent,
  type BookingScheduleChangeRequiredEvent,
  type BookingSeatReassignmentRequiredEvent,
  type BookingSeatShortageDetectedEvent,
  type BookingTransferEscalatedEvent,
  type BookingTransferredEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import { formatVietnamDateTime } from '@vietride/nest-common';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import { formatBookingReference } from './notification-display';

export type BookingTripChangeRoutingKey =
  | typeof BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY
  | typeof BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY
  | typeof BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY
  | typeof BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY
  | typeof BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY
  | typeof BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY
  | typeof BOOKING_TRANSFER_ESCALATED_ROUTING_KEY
  | typeof BOOKING_TRANSFERRED_ROUTING_KEY;

export function mapBookingTripChangeToNotification(
  routingKey: BookingTripChangeRoutingKey,
  payload: unknown,
  operatorRecipientUserId?: string,
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
    case BOOKING_SEAT_SHORTAGE_DETECTED_ROUTING_KEY:
      return mapSeatShortageDetected(
        requireOperatorRecipient(operatorRecipientUserId),
        BookingSeatShortageDetectedEventSchema.parse(payload),
      );
    case BOOKING_TRANSFER_ESCALATED_ROUTING_KEY:
      return mapTransferEscalated(
        requireOperatorRecipient(operatorRecipientUserId),
        BookingTransferEscalatedEventSchema.parse(payload),
      );
  }
}

function mapSeatShortageDetected(
  userId: string,
  payload: BookingSeatShortageDetectedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.VEHICLE_SUBSTITUTION_SEAT_SHORTAGE,
    title: 'Xe thay thế không đủ ghế',
    body: `Vé ${formatBookingReference(payload.bookingCode)} có ${payload.affectedPassengerCount} hành khách chưa được xếp ghế trên chuyến thay thế.`,
    data: operatorBookingEventData(payload),
  };
}

function mapTransferEscalated(
  userId: string,
  payload: BookingTransferEscalatedEvent,
): CreateNotificationDto {
  return {
    userId,
    type: NotificationType.BOOKING_TRANSFER_ESCALATED,
    title: 'Xác nhận chuyển khách quá hạn',
    body: `Vé ${formatBookingReference(payload.bookingCode)} có ${payload.pendingConfirmationCount} lượt chuyển khách chưa được tổ lái xác nhận.`,
    data: operatorBookingEventData(payload),
  };
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
      `Xe ${payload.newVehiclePlateNumber} khởi hành lúc ${formatVietnamDateTime(payload.newTripDepartureDateTime)}. ` +
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
      'Vì bạn chưa phản hồi, xe sẽ đưa bạn đến bến thay thế; ' +
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
    body: 'Lịch chuyến xe đã được tự động chấp nhận.',
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
    body: `Ghế ${payload.seatNumbers.join(', ')} của bạn trên chuyến xe cần được chọn lại.`,
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
    body: 'Giờ khởi hành chuyến xe đã thay đổi. Vui lòng kiểm tra lịch mới.',
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
    body: `Lịch chuyến xe đã thay đổi. Vui lòng phản hồi trước ${formatVietnamDateTime(payload.deadline)}.`,
    data: bookingEventData(payload),
  };
}

function mapPendingActionRealerted(payload: BookingPendingActionRealertedEvent): CreateNotificationDto {
  if (payload.reason === 'PENDING_SEAT_ASSIGNMENT') {
    return {
      userId: payload.userId,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: 'Nhắc lại: cần chọn lại ghế',
      body: `Bạn vẫn cần chọn lại ghế ${payload.seatNumbers.join(', ')} trước ${formatVietnamDateTime(payload.deadline)}.`,
      data: bookingEventData(payload),
    };
  }

  return {
    userId: payload.userId,
    type: NotificationType.TRIP_SCHEDULE_CHANGED,
    title: 'Nhắc lại: cần xác nhận lịch chuyến mới',
    body: `Bạn vẫn cần phản hồi về lịch chuyến xe trước ${formatVietnamDateTime(payload.deadline)}.`,
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

function operatorBookingEventData<T extends { operatorId: string }>(
  payload: T,
): T {
  return payload;
}

function requireOperatorRecipient(userId: string | undefined): string {
  if (!userId) throw new Error('OPERATOR_RECIPIENT_REQUIRED');
  return userId;
}
