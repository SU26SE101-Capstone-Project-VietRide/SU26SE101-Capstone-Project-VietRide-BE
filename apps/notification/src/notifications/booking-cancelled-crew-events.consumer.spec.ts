import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { BookingCancelledCrewEventsConsumer } from './booking-cancelled-crew-events.consumer';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const DRIVER_ID = '22222222-2222-4222-8222-222222222222';
const ASSISTANT_ID = '33333333-3333-4333-8333-333333333333';

describe('BookingCancelledCrewEventsConsumer', () => {
  it('creates one crew inbox/push job per unique assigned recipient', async () => {
    const notifications = { createNotification: jest.fn(async () => ({})) };
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, notifications, {
      crewUserIds: [DRIVER_ID, ASSISTANT_ID, DRIVER_ID],
      departureDateTime: '2026-08-08T03:00:00.000Z',
    });

    await consumer.handle(createPayload(), createMessage());

    expect(notifications.createNotification).toHaveBeenCalledTimes(2);
    expect(notifications.createNotification).toHaveBeenCalledWith(expect.objectContaining({
      userId: DRIVER_ID,
      type: NotificationType.BOOKING_CANCELLED,
      title: 'Vé trên chuyến đã bị hủy',
      body: 'Vé #VR-20260808-ABCDEFGH đã bị hủy và được gỡ khỏi danh sách đón khách.',
      data: expect.objectContaining({ seatNumbers: ['A01'] }),
    }));
    expect(idempotency.markProcessed).toHaveBeenCalledWith('booking.booking.cancelled:crew', EVENT_ID);
  });

  it.each([
    { previousStatus: 'PENDING_PAYMENT' },
    { cancellationReason: 'OPERATOR_CANCELLED_TRIP' },
  ])('skips a cancellation that does not require a manifest alert', async (override) => {
    const notifications = { createNotification: jest.fn() };
    const tripRecipients = { getTripRecipientSnapshot: jest.fn() };
    const consumer = createConsumer(createIdempotency(), notifications, undefined, tripRecipients);

    await consumer.handle(createPayload(override), createMessage());

    expect(notifications.createNotification).not.toHaveBeenCalled();
    expect(tripRecipients.getTripRecipientSnapshot).not.toHaveBeenCalled();
  });

  it('releases the owned lock when Trip lookup fails', async () => {
    const idempotency = createIdempotency();
    const consumer = createConsumer(idempotency, { createNotification: jest.fn() }, undefined, {
      getTripRecipientSnapshot: jest.fn(async () => { throw new Error('trip unavailable'); }),
    });

    await expect(consumer.handle(createPayload(), createMessage())).rejects.toThrow('trip unavailable');
    expect(idempotency.release).toHaveBeenCalledWith('booking.booking.cancelled:crew', EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });
});

function createConsumer(
  idempotency: ReturnType<typeof createIdempotency>,
  notifications: { createNotification: jest.Mock },
  snapshot = { crewUserIds: [DRIVER_ID], departureDateTime: '2026-08-08T03:00:00.000Z' },
  tripRecipients = { getTripRecipientSnapshot: jest.fn(async () => snapshot) },
) {
  return new BookingCancelledCrewEventsConsumer(
    { subscribe: jest.fn() } as never,
    idempotency as never,
    notifications as never,
    tripRecipients as never,
  );
}

function createIdempotency() {
  return {
    begin: jest.fn(async () => 'acquired' as const),
    markProcessed: jest.fn(async () => undefined),
    release: jest.fn(async () => undefined),
  };
}

function createPayload(overrides: Record<string, unknown> = {}) {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-08-08T01:00:00.000Z',
    bookingId: '44444444-4444-4444-8444-444444444444',
    bookingCode: 'VR-20260808-ABCDEFGH',
    userId: '55555555-5555-4555-8555-555555555555',
    refundAmount: 100000,
    refundOverride: false,
    cancellationReason: 'USER_INITIATED',
    ticketCodes: ['VT-20260808-ABCDEFGH'],
    ticketCount: 1,
    tripId: '66666666-6666-4666-8666-666666666666',
    previousStatus: 'CONFIRMED',
    seatNumbers: ['A01'],
    ...overrides,
  };
}

function createMessage(): ConsumeMessage {
  return { content: Buffer.from('{}'), properties: { messageId: 'broker-id' } } as ConsumeMessage;
}
