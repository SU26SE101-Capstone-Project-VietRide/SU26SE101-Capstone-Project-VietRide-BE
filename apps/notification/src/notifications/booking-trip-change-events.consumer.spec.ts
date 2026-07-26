import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
  BOOKING_SEAT_REASSIGNMENT_REQUIRED_ROUTING_KEY,
  BOOKING_TRANSFERRED_ROUTING_KEY,
  type BookingTransferredEvent,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  NotificationType,
  type Notification,
} from '../generated/notification-prisma-client';
import {
  BOOKING_TRIP_CHANGE_QUEUE_BINDINGS,
  BookingTripChangeEventsConsumer,
} from './booking-trip-change-events.consumer';
import { EmailSendQueue } from './email-send.queue';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const PENDING_ACTION_ID = '55555555-5555-4555-8555-555555555555';
const NEW_TRIP_ID = '66666666-6666-4666-8666-666666666666';
const VEHICLE_ID = '77777777-7777-4777-8777-777777777777';
const PASSENGER_ID = '88888888-8888-4888-8888-888888888888';
const OPERATOR_ID = '99999999-9999-4999-8999-999999999999';
const SUBSTITUTION_EVENT_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const NOTIFICATION_ID = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

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
      BOOKING_ROUTE_CHANGE_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      BOOKING_TRANSFERRED_ROUTING_KEY,
    ]);
    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(7);
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

  it('preserves correlationId fallback for existing Booking trip-change routing keys', async () => {
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
      createMessage(undefined, 'legacy-correlation-id'),
    );

    expect(idempotency.begin).toHaveBeenCalledWith(
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      'legacy-correlation-id',
      Buffer.from('{}'),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_SCHEDULE_CHANGE_INFORMATIONAL_ROUTING_KEY,
      'legacy-correlation-id',
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

  it('binds booking.booking.transferred and validates its strict shared schema', async () => {
    await consumer.onModuleInit();

    expect(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS).toContainEqual({
      queue: 'notification:booking-transferred',
      routingKey: BOOKING_TRANSFERRED_ROUTING_KEY,
    });
    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(7);

    idempotency.begin.mockResolvedValue('acquired');
    await consumer.handle(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      { ...createTransferredPayload(), unexpected: true },
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('notifyPassengers true creates one persisted and pushed Booking-owner notification and duplicate MessageId is a no-op', async () => {
    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');

    await consumer.handle(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      createTransferredPayload(),
      createMessage(EVENT_ID),
    );
    await consumer.handle(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      createTransferredPayload(),
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.VEHICLE_SUBSTITUTED,
        dedupeKey: `${BOOKING_TRANSFERRED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.VEHICLE_SUBSTITUTED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledTimes(1);
  });

  it('notifyPassengers false marks the event processed without notification or push', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await consumer.handle(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      { ...createTransferredPayload(), notifyPassengers: false },
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      EVENT_ID,
    );
  });

  it('malformed strict payload is marked processed with zero notification side effect and no retry', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        BOOKING_TRANSFERRED_ROUTING_KEY,
        { ...createTransferredPayload(), passengerName: 'must not cross the event boundary' },
        createMessage(EVENT_ID),
      ),
    ).resolves.toBeUndefined();

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      EVENT_ID,
    );
    expect(idempotency.release).not.toHaveBeenCalled();
  });

  it('missing MessageId fails before idempotency acquisition and notification side effects', async () => {
    await expect(
      consumer.handle(
        BOOKING_TRANSFERRED_ROUTING_KEY,
        createTransferredPayload(),
        createMessage(undefined, 'legacy-correlation-id'),
      ),
    ).rejects.toThrow(`MISSING_MESSAGE_ID_${BOOKING_TRANSFERRED_ROUTING_KEY}`);

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('downstream failure releases the acquired idempotency lock and rethrows for broker retry and DLQ', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockRejectedValue(new Error('FCM unavailable'));

    await expect(
      consumer.handle(
        BOOKING_TRANSFERRED_ROUTING_KEY,
        createTransferredPayload(),
        createMessage(EVENT_ID),
      ),
    ).rejects.toThrow('FCM unavailable');

    expect(idempotency.release).toHaveBeenCalledWith(BOOKING_TRANSFERRED_ROUTING_KEY, EVENT_ID);
    expect(idempotency.markProcessed).not.toHaveBeenCalled();
  });

  it('persisted row then enqueue failure then redelivery reuses the same row and marks the inbox processed after enqueue succeeds without a second row or effective job', async () => {
    const notification = createPersistedNotification();
    const repository = {
      create: jest
        .fn()
        .mockResolvedValueOnce({ notification, created: true })
        .mockResolvedValueOnce({ notification, created: false }),
    } as unknown as jest.Mocked<NotificationsRepository>;
    const effectiveJobs = new Set<string>();
    const fcmPushQueue = {
      enqueue: jest
        .fn()
        .mockRejectedValueOnce(new Error('Redis unavailable'))
        .mockImplementationOnce(async ({ notificationId }: { notificationId: string }) => {
          effectiveJobs.add(notificationId);
        }),
    } as unknown as jest.Mocked<FcmPushQueue>;
    const recoveringService = new NotificationsService(
      repository,
      fcmPushQueue,
      {} as EmailSendQueue,
      {} as EmailTemplateRenderer,
    );
    consumer = new BookingTripChangeEventsConsumer(
      rabbitConsumer,
      idempotency,
      recoveringService,
    );
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        BOOKING_TRANSFERRED_ROUTING_KEY,
        createTransferredPayload(),
        createMessage(EVENT_ID),
      ),
    ).rejects.toThrow('Redis unavailable');
    await expect(
      consumer.handle(
        BOOKING_TRANSFERRED_ROUTING_KEY,
        createTransferredPayload(),
        createMessage(EVENT_ID),
      ),
    ).resolves.toBeUndefined();

    expect(repository.create).toHaveBeenCalledTimes(2);
    expect(fcmPushQueue.enqueue).toHaveBeenCalledTimes(2);
    expect(effectiveJobs).toEqual(new Set([NOTIFICATION_ID]));
    expect(idempotency.release).toHaveBeenCalledTimes(1);
    expect(idempotency.markProcessed).toHaveBeenCalledTimes(1);
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_TRANSFERRED_ROUTING_KEY,
      EVENT_ID,
    );
  });
});

function createMessage(messageId: string | undefined, correlationId?: string): ConsumeMessage {
  return {
    content: Buffer.from('{}'),
    properties: { messageId, correlationId },
  } as ConsumeMessage;
}

function createTransferredPayload(): BookingTransferredEvent {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-26T01:00:00+00:00',
    sourceSubstitutionEventId: SUBSTITUTION_EVENT_ID,
    bookingId: BOOKING_ID,
    recipientUserId: USER_ID,
    operatorId: OPERATOR_ID,
    oldTripId: TRIP_ID,
    newTripId: NEW_TRIP_ID,
    newVehicleId: VEHICLE_ID,
    newVehiclePlateNumber: '51B-123.45',
    newTripDepartureDateTime: '2026-07-26T02:00:00+00:00',
    notifyPassengers: true,
    transfers: [
      {
        passengerId: PASSENGER_ID,
        originalSeatNumber: null,
        newSeatNumber: null,
        confirmationStatus: 'PENDING_CONFIRM',
      },
    ],
  };
}

function createPersistedNotification(): Notification {
  return {
    id: NOTIFICATION_ID,
    userId: USER_ID,
    type: NotificationType.VEHICLE_SUBSTITUTED,
    title: 'Xe thay the da duoc sap xep',
    body: 'Xe 51B-123.45 khoi hanh luc 2026-07-26T02:00:00+00:00.',
    data: createTransferredPayload(),
    dedupeKey: `${BOOKING_TRANSFERRED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.VEHICLE_SUBSTITUTED}`,
    readAt: null,
    createdAt: new Date('2026-07-26T01:00:00.000Z'),
  };
}
