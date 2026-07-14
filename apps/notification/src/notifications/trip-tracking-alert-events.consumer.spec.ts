import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import {
  TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
  TRIP_DELAYED_ROUTING_KEY,
  TRIP_STOP_DISABLED_ROUTING_KEY,
  TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
} from './trip-tracking-alert-events.constants';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const MESSAGE_ID = 'trip-alert-message-1';

describe('TripTrackingAlertEventsConsumer', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: TripTrackingAlertEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = {
      subscribe: jest.fn(),
    } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    consumer = new TripTrackingAlertEventsConsumer(
      rabbitConsumer,
      idempotency,
      notificationsService,
    );
  });

  it('subscribes all phase 5 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(
      TRIP_TRACKING_ALERT_QUEUE_BINDINGS.length,
    );
    for (const binding of TRIP_TRACKING_ALERT_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }

    expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
      'notification:booking-stop-disabled-affected',
      TRIP_STOP_DISABLED_ROUTING_KEY,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  });

  it('creates delayed notification for a new valid message', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockResolvedValue({
      id: '66666666-6666-4666-8666-666666666666',
      userId: USER_ID,
      type: NotificationType.TRIP_DELAYED,
      title: 'Chuyen xe bi tre',
      body: 'Chuyen xe bi tre',
      data: { tripId: TRIP_ID },
      readAt: null,
      createdAt: '2026-06-01T10:00:00.000Z',
    });

    await consumer.handle(
      TRIP_DELAYED_ROUTING_KEY,
      {
        userId: USER_ID,
        tripId: TRIP_ID,
        delayMinutes: 15,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.TRIP_DELAYED,
        dedupeKey: `${TRIP_DELAYED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.TRIP_DELAYED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(TRIP_DELAYED_ROUTING_KEY, MESSAGE_ID);
  });

  it('skips duplicate delayed message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      TRIP_DELAYED_ROUTING_KEY,
      {
        userId: USER_ID,
        tripId: TRIP_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('skips duplicate off-route message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      TRACKING_GPS_OFF_ROUTE_ROUTING_KEY,
      {
        userId: USER_ID,
        tripId: TRIP_ID,
        durationSeconds: 120,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops malformed payload without rethrowing', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        TRIP_DELAYED_ROUTING_KEY,
        {
          tripId: TRIP_ID,
          delayMinutes: 15,
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(TRIP_DELAYED_ROUTING_KEY, MESSAGE_ID);
  });
});

function createMessage(messageId: string | undefined): ConsumeMessage {
  return {
    properties: {
      messageId,
      correlationId: undefined,
    },
  } as ConsumeMessage;
}
