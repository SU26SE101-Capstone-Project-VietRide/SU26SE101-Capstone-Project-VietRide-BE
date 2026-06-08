import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_CONFIRMED_ROUTING_KEY,
  CORE_EVENT_QUEUE_BINDINGS,
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
} from './core-events.constants';
import { CoreEventsConsumer } from './core-events.consumer';
import { NotificationsService } from './notifications.service';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const MESSAGE_ID = 'message-1';

describe('CoreEventsConsumer', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let redisSet: jest.Mock;
  let redis: jest.Mocked<RedisService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: CoreEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = {
      subscribe: jest.fn(),
    } as unknown as jest.Mocked<RabbitMqConsumer>;
    redisSet = jest.fn();
    redis = {
      getClient: jest.fn(() => ({ set: redisSet })),
    } as unknown as jest.Mocked<RedisService>;
    notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    consumer = new CoreEventsConsumer(rabbitConsumer, redis, notificationsService);
  });

  it('subscribes all phase 4 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(CORE_EVENT_QUEUE_BINDINGS.length);
    for (const binding of CORE_EVENT_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1 },
      );
    }
  });

  it('creates notification for a new valid message', async () => {
    redisSet.mockResolvedValue('OK');
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

    expect(redisSet).toHaveBeenCalledWith(
      `notification:idem:${BOOKING_CONFIRMED_ROUTING_KEY}:${MESSAGE_ID}`,
      '1',
      'EX',
      RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
      'NX',
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'Dat ve thanh cong',
      }),
    );
  });

  it('skips duplicate message id', async () => {
    redisSet.mockResolvedValue(null);

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
    redisSet.mockResolvedValue('OK');

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
  });

  it('drops messages without id before idempotency check', async () => {
    await consumer.handle(
      BOOKING_CONFIRMED_ROUTING_KEY,
      {
        userId: USER_ID,
        bookingId: BOOKING_ID,
      },
      createMessage(undefined),
    );

    expect(redisSet).not.toHaveBeenCalled();
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
