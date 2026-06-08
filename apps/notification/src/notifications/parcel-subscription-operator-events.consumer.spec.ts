import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
} from './core-events.constants';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import {
  OPERATOR_RECIPIENT_PROVIDER,
  PARCEL_LOADED_ROUTING_KEY,
  PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
  SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
} from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const PARCEL_ID = '44444444-4444-4444-8444-444444444444';
const MESSAGE_ID = 'phase-6-message-1';

describe('ParcelSubscriptionOperatorEventsConsumer', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let redisSet: jest.Mock;
  let redis: jest.Mocked<RedisService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let operatorRecipientProvider: jest.Mocked<OperatorRecipientProvider>;
  let consumer: ParcelSubscriptionOperatorEventsConsumer;

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
    operatorRecipientProvider = {
      resolveOperatorRecipientUserIds: jest.fn(),
    };
    consumer = new ParcelSubscriptionOperatorEventsConsumer(
      rabbitConsumer,
      redis,
      notificationsService,
      operatorRecipientProvider,
    );
  });

  it('subscribes all phase 6 routing keys', async () => {
    await consumer.onModuleInit();

    expect(rabbitConsumer.subscribe).toHaveBeenCalledTimes(PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS.length);
    for (const binding of PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS) {
      expect(rabbitConsumer.subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1 },
      );
    }
  });

  it('creates parcel notification for a new valid message', async () => {
    redisSet.mockResolvedValue('OK');
    notificationsService.createNotification.mockResolvedValue(createNotification(NotificationType.PARCEL_LOADED));

    await consumer.handle(
      PARCEL_LOADED_ROUTING_KEY,
      {
        userId: USER_ID,
        parcelId: PARCEL_ID,
        parcelCode: 'PRC123',
      },
      createMessage(MESSAGE_ID),
    );

    expect(redisSet).toHaveBeenCalledWith(
      `notification:idem:${PARCEL_LOADED_ROUTING_KEY}:${MESSAGE_ID}`,
      '1',
      'EX',
      RABBITMQ_IDEMPOTENCY_TTL_SECONDS,
      'NX',
    );
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PARCEL_LOADED,
      }),
    );
  });

  it('uses operator recipient provider when payload only has operatorId', async () => {
    redisSet.mockResolvedValue('OK');
    operatorRecipientProvider.resolveOperatorRecipientUserIds.mockResolvedValue([USER_ID]);
    notificationsService.createNotification.mockResolvedValue(
      createNotification(NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED),
    );

    await consumer.handle(
      SUBSCRIPTION_LIMIT_TRIP_SKIPPED_ROUTING_KEY,
      {
        operatorId: OPERATOR_ID,
        planName: 'Starter',
      },
      createMessage(MESSAGE_ID),
    );

    expect(operatorRecipientProvider.resolveOperatorRecipientUserIds).toHaveBeenCalledWith(OPERATOR_ID);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.SUBSCRIPTION_LIMIT_EXCEEDED,
      }),
    );
  });

  it('skips duplicate message id', async () => {
    redisSet.mockResolvedValue(null);

    await consumer.handle(
      PARCEL_LOADED_ROUTING_KEY,
      {
        userId: USER_ID,
        parcelId: PARCEL_ID,
      },
      createMessage(MESSAGE_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops malformed payload without rethrowing', async () => {
    redisSet.mockResolvedValue('OK');

    await expect(
      consumer.handle(
        PARCEL_LOADED_ROUTING_KEY,
        {
          userId: USER_ID,
          parcelId: 'not-a-uuid',
        },
        createMessage(MESSAGE_ID),
      ),
    ).resolves.toBeUndefined();
    expect(notificationsService.createNotification).not.toHaveBeenCalled();
  });

  it('drops messages without id before idempotency check', async () => {
    await consumer.handle(
      PARCEL_LOADED_ROUTING_KEY,
      {
        userId: USER_ID,
        parcelId: PARCEL_ID,
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

function createNotification(type: NotificationType) {
  return {
    id: '99999999-9999-4999-8999-999999999999',
    userId: USER_ID,
    type,
    title: 'Title',
    body: 'Body',
    data: null,
    readAt: null,
    createdAt: '2026-06-01T10:00:00.000Z',
  };
}

void OPERATOR_RECIPIENT_PROVIDER;

