import { z } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateNotificationDto } from './dto/create-notification.dto';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  BOOKING_REFUNDED_ROUTING_KEY,
  WALLET_CREDITED_ROUTING_KEY,
  WALLET_DEBITED_ROUTING_KEY,
} from './core-events.constants';

const MoneyAmountSchema = z
  .union([z.number().int().nonnegative(), z.string().regex(/^\d+$/)])
  .optional();

const BookingEventPayloadSchema = z.object({
  userId: z.string().uuid(),
  bookingId: z.string().uuid(),
  tripId: z.string().uuid().optional(),
  bookingCode: z.string().trim().min(1).optional(),
  ticketCodes: z.array(z.string().trim().min(1)).optional(),
  ticketCount: z.number().int().nonnegative().optional(),
  routeName: z.string().trim().min(1).optional(),
  reason: z.string().trim().min(1).optional(),
  refundAmount: MoneyAmountSchema,
});

const WalletEventPayloadSchema = z.object({
  userId: z.string().uuid(),
  walletTransactionId: z.string().uuid().optional(),
  transactionId: z.string().uuid().optional(),
  referenceId: z.string().uuid().optional(),
  referenceType: z.string().trim().min(1).optional(),
  amount: MoneyAmountSchema,
  balanceAfter: MoneyAmountSchema,
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
      return mapBookingConfirmed(BookingEventPayloadSchema.parse(payload));
    case BOOKING_CANCELLED_ROUTING_KEY:
      return mapBookingCancelled(BookingEventPayloadSchema.parse(payload));
    case BOOKING_REFUNDED_ROUTING_KEY:
      return mapBookingRefunded(BookingEventPayloadSchema.parse(payload));
    case WALLET_CREDITED_ROUTING_KEY:
      return mapWalletCredited(WalletEventPayloadSchema.parse(payload));
    case WALLET_DEBITED_ROUTING_KEY:
      return mapWalletDebited(WalletEventPayloadSchema.parse(payload));
  }
}

function mapBookingConfirmed(
  payload: z.infer<typeof BookingEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_CONFIRMED,
    title: 'Dat ve thanh cong',
    body: `Ve ${formatBookingLabel(payload)} da duoc xac nhan.`,
    data: buildBookingData(payload),
  };
}

function mapBookingCancelled(
  payload: z.infer<typeof BookingEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.BOOKING_CANCELLED,
    title: 'Ve da bi huy',
    body: `Ve ${formatBookingLabel(payload)} da bi huy.${payload.reason ? ` Ly do: ${payload.reason}.` : ''}`,
    data: buildBookingData(payload),
  };
}

function mapBookingRefunded(
  payload: z.infer<typeof BookingEventPayloadSchema>,
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
  payload: z.infer<typeof WalletEventPayloadSchema>,
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
  payload: z.infer<typeof WalletEventPayloadSchema>,
): CreateNotificationDto {
  return {
    userId: payload.userId,
    type: NotificationType.WALLET_DEBITED,
    title: 'Vi da bi tru tien',
    body: `Vi VietRide cua ban vua bi tru ${formatMoney(payload.amount)} VND.`,
    data: buildWalletData(payload),
  };
}

function formatBookingLabel(payload: z.infer<typeof BookingEventPayloadSchema>): string {
  return payload.bookingCode ? `#${payload.bookingCode}` : payload.bookingId;
}

function formatMoney(amount: z.infer<typeof MoneyAmountSchema>): string {
  if (amount === undefined) {
    return '0';
  }

  return amount.toString();
}

function buildBookingData(
  payload: z.infer<typeof BookingEventPayloadSchema>,
): Record<string, unknown> {
  return {
    bookingId: payload.bookingId,
    tripId: payload.tripId ?? null,
    bookingCode: payload.bookingCode ?? null,
    ticketCodes: payload.ticketCodes ?? null,
    ticketCount: payload.ticketCount ?? payload.ticketCodes?.length ?? null,
    routeName: payload.routeName ?? null,
    reason: payload.reason ?? null,
    refundAmount: payload.refundAmount ?? null,
  };
}

function buildWalletData(
  payload: z.infer<typeof WalletEventPayloadSchema>,
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
