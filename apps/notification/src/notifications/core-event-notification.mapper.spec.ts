import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import { mapCoreEventToNotification } from './core-event-notification.mapper';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  BOOKING_REFUNDED_ROUTING_KEY,
  WALLET_CREDITED_ROUTING_KEY,
  WALLET_DEBITED_ROUTING_KEY,
} from './core-events.constants';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const WALLET_TRANSACTION_ID = '44444444-4444-4444-8444-444444444444';

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
      title: 'Dat ve thanh cong',
      body: 'Ve #VR123 da duoc xac nhan.',
      data: {
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        bookingCode: 'VR123',
        routeName: 'Sai Gon - Da Lat',
        reason: null,
        refundAmount: null,
      },
    });
  });

  it('maps booking cancelled event', () => {
    expect(
      mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, {
        userId: USER_ID,
        bookingId: BOOKING_ID,
        reason: 'Passenger cancelled',
      }),
    ).toEqual(
      expect.objectContaining({
        type: NotificationType.BOOKING_CANCELLED,
        title: 'Ve da bi huy',
        body: `Ve ${BOOKING_ID} da bi huy. Ly do: Passenger cancelled.`,
      }),
    );
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
        title: 'Hoan tien ve thanh cong',
        body: `Khoan hoan tien cho ve ${BOOKING_ID} da duoc ghi nhan. So tien hoan: 120000 VND.`,
      }),
    );
  });

  it('maps wallet credited event', () => {
    expect(
      mapCoreEventToNotification(WALLET_CREDITED_ROUTING_KEY, {
        userId: USER_ID,
        walletTransactionId: WALLET_TRANSACTION_ID,
        amount: 50000,
        balanceAfter: 150000,
        referenceType: 'TOP_UP',
      }),
    ).toEqual(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.WALLET_CREDITED,
        title: 'Vi da duoc cong tien',
        body: 'Vi VietRide cua ban vua duoc cong 50000 VND.',
        data: expect.objectContaining({
          walletTransactionId: WALLET_TRANSACTION_ID,
          amount: 50000,
          balanceAfter: 150000,
          referenceType: 'TOP_UP',
        }),
      }),
    );
  });

  it('maps wallet debited event', () => {
    expect(
      mapCoreEventToNotification(WALLET_DEBITED_ROUTING_KEY, {
        userId: USER_ID,
        transactionId: WALLET_TRANSACTION_ID,
        amount: '75000',
      }),
    ).toEqual(
      expect.objectContaining({
        type: NotificationType.WALLET_DEBITED,
        title: 'Vi da bi tru tien',
        body: 'Vi VietRide cua ban vua bi tru 75000 VND.',
        data: expect.objectContaining({
          walletTransactionId: WALLET_TRANSACTION_ID,
          amount: '75000',
        }),
      }),
    );
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
