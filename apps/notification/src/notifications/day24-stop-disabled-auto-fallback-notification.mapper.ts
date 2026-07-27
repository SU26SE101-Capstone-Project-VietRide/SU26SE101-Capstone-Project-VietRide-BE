import {
  BookingStopDisabledAutoFallbackAppliedEventSchema,
  type BookingStopDisabledAutoFallbackAppliedEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';

export function mapStopDisabledAutoFallbackToNotification(payload: unknown): CreateNotificationDto {
  return mapParsedFallback(BookingStopDisabledAutoFallbackAppliedEventSchema.parse(payload));
}

function mapParsedFallback(
  payload: BookingStopDisabledAutoFallbackAppliedEvent,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.STOP_DISABLED,
    title: 'Đã tự động chuyển về bến',
    body: `Vì bạn không phản hồi, vé ${payload.bookingId} đã được chuyển về bến ${payload.fallbackStationId}.`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      eventType: payload.eventType,
      bookingId: payload.bookingId,
      tripId: payload.tripId,
      pendingActionId: payload.pendingActionId,
      disabledStopId: payload.disabledStopId,
      affectedField: payload.affectedField,
      fallbackStationId: payload.fallbackStationId,
      resolvedAction: payload.resolvedAction,
    },
  };
}
