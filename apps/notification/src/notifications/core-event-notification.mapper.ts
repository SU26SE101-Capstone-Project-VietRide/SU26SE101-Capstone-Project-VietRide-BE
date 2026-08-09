import { z } from 'zod';
import {
  BookingCancelledConsumerEventSchema,
  BookingDisruptedEventSchema,
  type BookingCancelledConsumerEvent,
  type BookingDisruptedEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  BOOKING_DISRUPTED_ROUTING_KEY,
  BOOKING_REFUNDED_ROUTING_KEY,
  WALLET_CREDITED_ROUTING_KEY,
  WALLET_DEBITED_ROUTING_KEY,
} from './core-events.constants';
import { formatBookingReference } from './notification-display';

const moneyAmountSchema = z
  .union([z.number().int().nonnegative(), z.string().regex(/^\d+$/)])
  .optional();

const bookingEventPayloadSchema = z.object({
  userId: z.string().uuid(),
  bookingId: z.string().uuid(),
  tripId: z.string().uuid().optional(),
  bookingCode: z.string().trim().min(1).optional(),
  ticketCodes: z.array(z.string().trim().min(1)).optional(),
  ticketCount: z.number().int().nonnegative().optional(),
  routeName: z.string().trim().min(1).optional(),
  reason: z.string().trim().min(1).optional(),
  refundAmount: moneyAmountSchema,
});

const walletEventPayloadSchema = z.object({
  userId: z.string().uuid(),
  walletTransactionId: z.string().uuid().optional(),
  transactionId: z.string().uuid().optional(),
  referenceId: z.string().uuid().optional(),
  referenceType: z.string().trim().min(1).optional(),
  amount: moneyAmountSchema,
  balanceAfter: moneyAmountSchema,
  note: z.string().trim().min(1).optional(),
});

export type CoreEventRoutingKey =
  | typeof BOOKING_CONFIRMED_ROUTING_KEY
  | typeof BOOKING_CANCELLED_ROUTING_KEY
  | typeof BOOKING_DISRUPTED_ROUTING_KEY
  | typeof BOOKING_REFUNDED_ROUTING_KEY
  | typeof WALLET_CREDITED_ROUTING_KEY
  | typeof WALLET_DEBITED_ROUTING_KEY;

export function mapCoreEventToNotification(
  routingKey: CoreEventRoutingKey,
  payload: unknown,
): CreateNotificationDto {
  switch (routingKey) {
    case BOOKING_CONFIRMED_ROUTING_KEY:
      return mapBookingConfirmed(bookingEventPayloadSchema.parse(payload));
    case BOOKING_CANCELLED_ROUTING_KEY:
      return mapBookingCancelled(BookingCancelledConsumerEventSchema.parse(payload));
    case BOOKING_DISRUPTED_ROUTING_KEY:
      return mapBookingDisrupted(BookingDisruptedEventSchema.parse(payload));
    case BOOKING_REFUNDED_ROUTING_KEY:
      return mapBookingRefunded(bookingEventPayloadSchema.parse(payload));
    case WALLET_CREDITED_ROUTING_KEY:
      return mapWalletCredited(walletEventPayloadSchema.parse(payload));
    case WALLET_DEBITED_ROUTING_KEY:
      return mapWalletDebited(walletEventPayloadSchema.parse(payload));
  }
}

function mapBookingConfirmed(
  payload: z.infer<typeof bookingEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_CONFIRMED,
    title: 'Đặt vé thành công',
    body: `Vé ${formatBookingReference(payload.bookingCode)} đã được xác nhận.`,
    data: buildBookingData(payload),
  };
}

function mapBookingCancelled(payload: BookingCancelledConsumerEvent): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_CANCELLED,
    title: 'Vé đã bị hủy',
    body: `Vé ${formatBookingReference(payload.bookingCode)} đã bị hủy. Lý do: ${payload.cancellationReason}.`,
    data: buildBookingData(payload),
  };
}

function mapBookingDisrupted(payload: BookingDisruptedEvent): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_DISRUPTED,
    title: 'Chuyến đi bị gián đoạn',
    body: `Vé #${payload.bookingCode} bị gián đoạn. Số tiền hoàn dự kiến: ${formatMoney(payload.refundAmount)} VND.`,
    data: {
      eventId: payload.eventId,
      occurredAt: payload.occurredAt,
      bookingId: payload.bookingId,
      bookingCode: payload.bookingCode,
      tripId: payload.tripId,
      operatorId: payload.operatorId,
      traveledRatio: payload.traveledRatio,
      refundAmount: payload.refundAmount,
      cancellationReason: payload.cancellationReason,
    },
  };
}

function mapBookingRefunded(
  payload: z.infer<typeof bookingEventPayloadSchema>,
): CreateNotificationDto {
  const refundText = payload.refundAmount
    ? ` Số tiền hoàn: ${formatMoney(payload.refundAmount)} VND.`
    : '';

  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_REFUNDED,
    title: 'Hoàn tiền vé thành công',
    body: `Khoản hoàn tiền cho vé ${formatBookingReference(payload.bookingCode)} đã được ghi nhận.${refundText}`,
    data: buildBookingData(payload),
  };
}

function mapWalletCredited(
  payload: z.infer<typeof walletEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.WALLET_CREDITED,
    title: 'Ví đã được cộng tiền',
    body: `Ví VietRide của bạn vừa được cộng ${formatMoney(payload.amount)} VND.`,
    data: buildWalletData(payload),
  };
}

function mapWalletDebited(
  payload: z.infer<typeof walletEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.WALLET_DEBITED,
    title: 'Ví đã bị trừ tiền',
    body: `Ví VietRide của bạn vừa bị trừ ${formatMoney(payload.amount)} VND.`,
    data: buildWalletData(payload),
  };
}

function formatMoney(amount: z.infer<typeof moneyAmountSchema>): string {
  if (amount === undefined) {
    return '0';
  }

  return amount.toString();
}

function buildBookingData(
  payload: {
    bookingId: string;
    bookingCode?: string | undefined;
    ticketCodes?: string[] | undefined;
    ticketCount?: number | undefined;
    refundAmount?: z.infer<typeof moneyAmountSchema>;
    tripId?: string | undefined;
    routeName?: string | undefined;
    reason?: string | undefined;
    cancellationReason?: string | undefined;
  },
): Record<string, unknown> {
  return {
    bookingId: payload.bookingId,
    tripId: payload.tripId ?? null,
    bookingCode: payload.bookingCode ?? null,
    ticketCodes: payload.ticketCodes ?? null,
    ticketCount: payload.ticketCount ?? payload.ticketCodes?.length ?? null,
    routeName: payload.routeName ?? null,
    reason: 'cancellationReason' in payload ? payload.cancellationReason : payload.reason ?? null,
    refundAmount: payload.refundAmount ?? null,
  };
}

function buildWalletData(
  payload: z.infer<typeof walletEventPayloadSchema>,
): Record<string, unknown> {
  return {
    walletTransactionId: payload.walletTransactionId ?? payload.transactionId ?? null,
    referenceId: payload.referenceId ?? null,
    referenceType: payload.referenceType ?? null,
    amount: payload.amount ?? null,
    balanceAfter: payload.balanceAfter ?? null,
    note: payload.note ?? null,
  };
}
