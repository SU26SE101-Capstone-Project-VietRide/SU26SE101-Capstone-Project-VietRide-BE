import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
  type BookingStopDisabledAutoFallbackAppliedEvent,
} from '@vietride/contracts';
import { Day24StopDisabledAutoFallbackEventsConsumer } from './day24-stop-disabled-auto-fallback-events.consumer';
import { DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING } from './day24-stop-disabled-auto-fallback-events.consumer';
import { mapStopDisabledAutoFallbackToNotification } from './day24-stop-disabled-auto-fallback-notification.mapper';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const PENDING_ACTION_ID = '55555555-5555-4555-8555-555555555555';
const DISABLED_STOP_ID = '66666666-6666-4666-8666-666666666666';
const FALLBACK_STATION_ID = '77777777-7777-4777-8777-777777777777';

describe('Day 24 fallback notification:', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: Day24StopDisabledAutoFallbackEventsConsumer;

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
    consumer = new Day24StopDisabledAutoFallbackEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
    );
  });

  it('maps userId as the sole recipient with exact fallback metadata', () => {
    expect(mapStopDisabledAutoFallbackToNotification(canonicalPayload())).toEqual({
      userId: USER_ID,
      type: NotificationType.STOP_DISABLED,
      title: 'Đã tự động chuyển về bến',
      body: `Vì bạn không phản hồi, vé ${BOOKING_ID} đã được chuyển về bến ${FALLBACK_STATION_ID}.`,
      data: {
        eventId: EVENT_ID,
        occurredAt: '2026-07-18T10:00:00+07:00',
        eventType: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        pendingActionId: PENDING_ACTION_ID,
        disabledStopId: DISABLED_STOP_ID,
        affectedField: 'PICKUP',
        fallbackStationId: FALLBACK_STATION_ID,
        resolvedAction: 'AUTO_FALLBACK_DESTINATION',
      },
    });
  });

  it('dedupes redelivery by EventId before creating a duplicate notification', async () => {
    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');

    await consumer.handle(canonicalPayload(), createMessage(EVENT_ID));
    await consumer.handle(canonicalPayload(), createMessage(EVENT_ID));

    expect(idempotency.begin).toHaveBeenNthCalledWith(
      1,
      BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      EVENT_ID,
      undefined,
    );
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      2,
      BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      EVENT_ID,
      undefined,
    );
    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.STOP_DISABLED,
        dedupeKey: `${BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.STOP_DISABLED}`,
      }),
    );
  });

  it('marks malformed events processed without a notification', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await consumer.handle(
      { ...canonicalPayload(), affectedField: 'INVALID' },
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
      EVENT_ID,
    );
  });
});

function canonicalPayload(): BookingStopDisabledAutoFallbackAppliedEvent {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: BOOKING_STOP_DISABLED_AUTO_FALLBACK_APPLIED_ROUTING_KEY,
    bookingId: BOOKING_ID,
    tripId: TRIP_ID,
    userId: USER_ID,
    pendingActionId: PENDING_ACTION_ID,
    disabledStopId: DISABLED_STOP_ID,
    affectedField: 'PICKUP' as const,
    fallbackStationId: FALLBACK_STATION_ID,
    resolvedAction: 'AUTO_FALLBACK_DESTINATION' as const,
  };
}

function createMessage(messageId: string): ConsumeMessage {
  return { properties: { messageId, correlationId: undefined } } as ConsumeMessage;
}

void DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING;
