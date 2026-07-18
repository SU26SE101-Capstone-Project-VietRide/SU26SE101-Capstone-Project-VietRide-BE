import { z } from 'zod';
import {
  BookingCancelledConsumerEventSchema,
  type BookingCancelledConsumerEvent,
} from '@vietride/contracts';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  BOOKING_REFUNDED_ROUTING_KEY,
  WALLET_CREDITED_ROUTING_KEY,
  WALLET_DEBITED_ROUTING_KEY,
} from './core-events.constants';

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
    title: 'Dat ve thanh cong',
    body: `Ve ${formatBookingLabel(payload)} da duoc xac nhan.`,
    data: buildBookingData(payload),
  };
}

function mapBookingCancelled(payload: BookingCancelledConsumerEvent): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_CANCELLED,
    title: 'Ve da bi huy',
    body: `Ve ${formatBookingLabel(payload)} da bi huy. Ly do: ${payload.cancellationReason}.`,
    data: buildBookingData(payload),
  };
}

function mapBookingRefunded(
  payload: z.infer<typeof bookingEventPayloadSchema>,
): CreateNotificationDto {
  const refundText = payload.refundAmount
    ? ` So tien hoan: ${formatMoney(payload.refundAmount)} VND.`
    : '';

  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_REFUNDED,
    title: 'Hoan tien ve thanh cong',
    body: `Khoan hoan tien cho ve ${formatBookingLabel(payload)} da duoc ghi nhan.${refundText}`,
    data: buildBookingData(payload),
  };
}

function mapWalletCredited(
  payload: z.infer<typeof walletEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.WALLET_CREDITED,
    title: 'Vi da duoc cong tien',
    body: `Vi VietRide cua ban vua duoc cong ${formatMoney(payload.amount)} VND.`,
    data: buildWalletData(payload),
  };
}

function mapWalletDebited(
  payload: z.infer<typeof walletEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.WALLET_DEBITED,
    title: 'Vi da bi tru tien',
    body: `Vi VietRide cua ban vua bi tru ${formatMoney(payload.amount)} VND.`,
    data: buildWalletData(payload),
  };
}

function formatBookingLabel(payload: { bookingCode?: string | undefined; bookingId: string }): string {
  return payload.bookingCode ? `#${payload.bookingCode}` : payload.bookingId;
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
