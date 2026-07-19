import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_TRIP_CHANGE_QUEUE_BINDINGS,
  BookingTripChangeEventsConsumer,
} from './booking-trip-change-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const PENDING_ACTION_ID = '55555555-5555-4555-8555-555555555555';

describe('BookingTripChangeEventsConsumer binds the Booking-owned passenger facts', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: BookingTripChangeEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = { subscribe: jest.fn() } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    consumer = new BookingTripChangeEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
    );
  });

  it('binds the Booking-owned passenger facts', async () => {
    await consumer.onModuleInit();

    expect(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.map(({ routingKey }) => routingKey)).toEqual([
      BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
    ]);
    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(5);
    for (const binding of BOOKING_TRIP_CHANGE_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }
    expect(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.map(({ routingKey }) => routingKey)).not.toContain(
      'booking.booking.stop_disabled_auto_fallback_applied',
    );
    expect(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.map(({ routingKey }) => routingKey)).not.toContain(
      'booking.booking.passenger_no_show_marked',
    );
  });

  it('creates one passenger informational notification without pending-action data', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await consumer.handle(
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      {
        eventId: EVENT_ID,
        occurredAt: '2026-07-15T01:00:00+00:00',
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        userId: USER_ID,
        oldDeparture: '2026-07-16T01:00:00+00:00',
        newDeparture: '2026-07-16T01:15:00+00:00',
        severity: 'MINOR',
      },
      createMessage('informational-message'),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_SCHEDULE_CHANGED,
        dedupeKey: `${BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY}:informational-message:${USER_ID}:${NotificationType.TRIP_SCHEDULE_CHANGED}`,
      }),
    );
    const created = notificationsService.createNotification.mock.calls[0]?.[0];
    expect(created?.data).not.toHaveProperty('pendingActionId');
    expect(created?.data).not.toHaveProperty('deadline');
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      'informational-message',
    );
  });

  it('uses the broker MessageId in each physical re-alert dedupe key', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    const payload = {
      eventId: EVENT_ID,
      occurredAt: '2026-07-15T03:00:00+00:00',
      bookingId: BOOKING_ID,
      tripId: TRIP_ID,
      userId: USER_ID,
      pendingActionId: PENDING_ACTION_ID,
      deadline: '2026-07-15T05:00:00+00:00',
      reason: 'SCHEDULE_CHANGE',
      oldDeparture: '2026-07-16T01:00:00+00:00',
      newDeparture: '2026-07-16T03:00:00+00:00',
      severity: 'MAJOR',
    };

    await consumer.handle(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      payload,
      createMessage('physical-job-1'),
    );
    await consumer.handle(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      payload,
      createMessage('physical-job-2'),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({
        dedupeKey: `${BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY}:physical-job-1:${USER_ID}:${NotificationType.TRIP_SCHEDULE_CHANGED}`,
      }),
    );
    expect(notificationsService.createNotification).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        dedupeKey: `${BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY}:physical-job-2:${USER_ID}:${NotificationType.TRIP_SCHEDULE_CHANGED}`,
      }),
    );
  });

  it('skips a redelivered message already marked as processed', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
      {},
      createMessage('redelivered-message'),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('marks malformed discriminated payloads processed without creating a notification', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
        {
          eventId: EVENT_ID,
          occurredAt: '2026-07-15T03:00:00+00:00',
          bookingId: BOOKING_ID,
          tripId: TRIP_ID,
          userId: USER_ID,
          pendingActionId: PENDING_ACTION_ID,
          deadline: '2026-07-15T05:00:00+00:00',
          reason: 'PENDING_SEAT_ASSIGNMENT',
          oldDeparture: '2026-07-16T01:00:00+00:00',
          newDeparture: '2026-07-16T03:00:00+00:00',
          severity: 'MAJOR',
        },
        createMessage('malformed-message'),
      ),
    ).resolves.toBeUndefined();

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      'malformed-message',
    );
  });
});

function createMessage(messageId: string): ConsumeMessage {
  return {
    properties: { messageId, correlationId: undefined },
  } as ConsumeMessage;
}
