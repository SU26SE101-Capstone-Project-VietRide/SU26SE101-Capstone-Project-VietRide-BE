import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import { mapCoreEventToNotification } from './core-event-notification.mapper';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  BOOKING_DISRUPTED_ROUTING_KEY,
  BOOKING_REFUNDED_ROUTING_KEY,
  WALLET_CREDITED_ROUTING_KEY,
  WALLET_DEBITED_ROUTING_KEY,
} from './core-events.constants';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const WALLET_TRANSACTION_ID = '44444444-4444-4444-8444-444444444444';
const OPERATOR_ID = '55555555-5555-4555-8555-555555555555';
const EVENT_ID = '66666666-6666-4666-8666-666666666666';

describe('mapCoreEventToNotification', () => {
  it('maps booking confirmed event', () => {
    expect(
      mapCoreEventToNotification(BOOKING_CONFIRMED_ROUTING_KEY, {
        userId: USER_ID,
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        bookingCode: 'VR123',
        routeName: 'Sai Gon - Da Lat',
      }),
    ).toEqual({
      userId: USER_ID,
      type: NotificationType.BOOKING_CONFIRMED,
      title: 'Đặt vé thành công',
      body: 'Vé #VR123 đã được xác nhận.',
      data: {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        bookingCode: 'VR123',
        ticketCodes: null,
        ticketCount: null,
        routeName: 'Sai Gon - Da Lat',
        reason: null,
        refundAmount: null,
      },
    });
  });

  it('maps booking ticket metadata when present', () => {
    const notification = mapCoreEventToNotification(BOOKING_CONFIRMED_ROUTING_KEY, {
      userId: USER_ID,
      bookingId: BOOKING_ID,
      tripId: TRIP_ID,
      bookingCode: 'VR123',
      ticketCodes: ['VT-20260706-ABCDEFGH', 'VT-20260706-HGFEDCBA'],
      ticketCount: 2,
    });

    expect(notification.data).toMatchObject({
      ticketCodes: ['VT-20260706-ABCDEFGH', 'VT-20260706-HGFEDCBA'],
      ticketCount: 2,
    });
  });

  it('maps booking cancelled event', () => {
    expect(
      mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, {
        eventId: '55555555-5555-4555-8555-555555555555',
        occurredAt: '2026-07-17T00:00:00+00:00',
        userId: USER_ID,
        bookingId: BOOKING_ID,
        refundAmount: 0,
        refundOverride: false,
        cancellationReason: 'Passenger cancelled',
      }),
    ).toEqual(
      expect.objectContaining({
        type: NotificationType.BOOKING_CANCELLED,
        title: 'Vé đã bị hủy',
        body: `Vé ${BOOKING_ID} đã bị hủy. Lý do: Passenger cancelled.`,
      }),
    );
  });

  it('keeps Booking cancellation as the passenger cancellation notification path', () => {
    const notification = mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, {
      userId: USER_ID,
      bookingId: BOOKING_ID,
      refundAmount: 0,
      refundOverride: true,
      cancellationReason: 'DRIVER_SCHEDULE_DAY_REMOVED',
    });

    expect(notification).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.BOOKING_CANCELLED,
      }),
    );
    expect(notification.data).toMatchObject({ bookingId: BOOKING_ID, refundAmount: 0 });
  });

  it('maps booking disruption to the dedicated passenger notification type', () => {
    expect(
      mapCoreEventToNotification(BOOKING_DISRUPTED_ROUTING_KEY, {
        eventId: EVENT_ID,
        occurredAt: '2026-07-30T03:00:01Z',
        bookingId: BOOKING_ID,
        bookingCode: 'VR-20260730-ABCDEFGH',
        tripId: TRIP_ID,
        operatorId: OPERATOR_ID,
        userId: USER_ID,
        traveledRatio: 0.4,
        refundAmount: 300_000,
        cancellationReason: 'OPERATOR_DISRUPTED_IN_PROGRESS',
      }),
    ).toEqual({
      userId: USER_ID,
      type: NotificationType.BOOKING_DISRUPTED,
      title: 'Chuyến đi bị gián đoạn',
      body: 'Vé #VR-20260730-ABCDEFGH bị gián đoạn. Số tiền hoàn dự kiến: 300000 VND.',
      data: {
        eventId: EVENT_ID,
        occurredAt: '2026-07-30T03:00:01Z',
        bookingId: BOOKING_ID,
        bookingCode: 'VR-20260730-ABCDEFGH',
        tripId: TRIP_ID,
        operatorId: OPERATOR_ID,
        traveledRatio: 0.4,
        refundAmount: 300_000,
        cancellationReason: 'OPERATOR_DISRUPTED_IN_PROGRESS',
      },
    });
  });

  it('maps booking refunded event', () => {
    expect(
      mapCoreEventToNotification(BOOKING_REFUNDED_ROUTING_KEY, {
        userId: USER_ID,
        bookingId: BOOKING_ID,
        refundAmount: '120000',
      }),
    ).toEqual(
      expect.objectContaining({
        type: NotificationType.BOOKING_REFUNDED,
        title: 'Hoàn tiền vé thành công',
        body: `Khoản hoàn tiền cho vé ${BOOKING_ID} đã được ghi nhận. Số tiền hoàn: 120000 VND.`,
      }),
    );
  });

  it('maps wallet credited event', () => {
    const notification = mapCoreEventToNotification(WALLET_CREDITED_ROUTING_KEY, {
      userId: USER_ID,
      walletTransactionId: WALLET_TRANSACTION_ID,
      amount: 50000,
      balanceAfter: 150000,
      referenceType: 'TOP_UP',
    });

    expect(notification).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.WALLET_CREDITED,
        title: 'Ví đã được cộng tiền',
        body: 'Ví VietRide của bạn vừa được cộng 50000 VND.',
      }),
    );
    expect(notification.data).toMatchObject({
      walletTransactionId: WALLET_TRANSACTION_ID,
      amount: 50000,
      balanceAfter: 150000,
      referenceType: 'TOP_UP',
    });
  });

  it('maps wallet debited event', () => {
    const notification = mapCoreEventToNotification(WALLET_DEBITED_ROUTING_KEY, {
      userId: USER_ID,
      transactionId: WALLET_TRANSACTION_ID,
      amount: '75000',
    });

    expect(notification).toEqual(
      expect.objectContaining({
        type: NotificationType.WALLET_DEBITED,
        title: 'Ví đã bị trừ tiền',
        body: 'Ví VietRide của bạn vừa bị trừ 75000 VND.',
      }),
    );
    expect(notification.data).toMatchObject({
      walletTransactionId: WALLET_TRANSACTION_ID,
      amount: '75000',
    });
  });

  it('rejects malformed payload', () => {
    expect(() =>
      mapCoreEventToNotification(BOOKING_CONFIRMED_ROUTING_KEY, {
        userId: 'not-a-uuid',
        bookingId: BOOKING_ID,
      }),
    ).toThrow(ZodError);
  });
});
