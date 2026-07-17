import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_CONFIRMED_ROUTING_KEY,
  CORE_EVENT_QUEUE_BINDINGS,
} from './core-events.constants';
import { CoreEventsConsumer } from './core-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const MESSAGE_ID = 'message-1';

describe('CoreEventsConsumer', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: CoreEventsConsumer;

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
    consumer = new CoreEventsConsumer(rabbitConsumer, idempotency, notificationsService);
  });

  it('subscribes all phase 4 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(CORE_EVENT_QUEUE_BINDINGS.length);
    for (const binding of CORE_EVENT_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }
    expect(CORE_EVENT_QUEUE_BINDINGS).toContainEqual({
      queue: 'notification:booking-cancelled',
      routingKey: BOOKING_CANCELLED_ROUTING_KEY,
    });
    expect(CORE_EVENT_QUEUE_BINDINGS).not.toEqual(
      expect.arrayContaining([expect.objectContaining({ routingKey: 'trip.trip.cancelled' })]),
    );
  });

  it('creates notification for a new valid message', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockResolvedValue({
      id: '33333333-3333-4333-8333-333333333333',
      userId: USER_ID,
      type: NotificationType.BOOKING_CONFIRMED,
      title: 'Dat ve thanh cong',
      body: 'Ve #VR123 da duoc xac nhan.',
      data: { bookingId: BOOKING_ID },
      readAt: null,
      createdAt: '2026-06-01T10:00:00.000Z',
    });

    await consumer.handle(
      BOOKING_CONFIRMED_ROUTING_KEY,
      {
        userId: USER_ID,
        bookingId: BOOKING_ID,
        bookingCode: 'VR123',
      },
      createMessage(MESSAGE_ID),
    );

    expect(idempotency.begin).toHaveBeenCalledWith(BOOKING_CONFIRMED_ROUTING_KEY, MESSAGE_ID);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'Dat ve thanh cong',
        dedupeKey: `${BOOKING_CONFIRMED_ROUTING_KEY}:${MESSAGE_ID}:${USER_ID}:${NotificationType.BOOKING_CONFIRMED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_CONFIRMED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('skips duplicate message id', async () => {
    idempotency.begin.mockResolvedValue('duplicate');

    await consumer.handle(
      BOOKING_CONFIRMED_ROUTING_KEY,
      {
        userId: USER_ID,
        bookingId: BOOKING_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops malformed payload without rethrowing', async () => {
    idempotency.begin.mockResolvedValue('acquired');

    await expect(
      consumer.handle(
        BOOKING_CONFIRMED_ROUTING_KEY,
        {
          userId: 'not-a-uuid',
          bookingId: BOOKING_ID,
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_CONFIRMED_ROUTING_KEY,
      MESSAGE_ID,
    );
  });

  it('releases processing lock and rethrows transient failures', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    notificationsService.createNotification.mockRejectedValue(new Error('DB_DOWN'));

    await expect(
      consumer.handle(
        BOOKING_CONFIRMED_ROUTING_KEY,
        {
          userId: USER_ID,
          bookingId: BOOKING_ID,
          bookingCode: 'VR123',
        },
        createMessage(MESSAGE_ID),
      ),
    ).rejects.toThrow('DB_DOWN');

    expect(idempotency.release).toHaveBeenCalledWith(BOOKING_CONFIRMED_ROUTING_KEY, MESSAGE_ID);
  });

  it('rejects messages without id before idempotency check', async () => {
    await expect(
      consumer.handle(
        BOOKING_CONFIRMED_ROUTING_KEY,
        {
          userId: USER_ID,
          bookingId: BOOKING_ID,
        },
        createMessage(undefined),
      ),
    ).rejects.toThrow('MISSING_MESSAGE_ID');

    expect(idempotency.begin).not.toHaveBeenCalled();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
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
