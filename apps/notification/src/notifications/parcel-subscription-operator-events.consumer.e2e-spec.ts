import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import { NotificationsService } from './notifications.service';
import type { OperatorRecipientProvider } from './operator-recipient.provider';
import {
  OPERATOR_RECIPIENT_PROVIDER,
  PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
} from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';

describe('ParcelSubscriptionOperatorEventsConsumer registration (e2e)', () => {
  it('registers parcel/subscription/operator subscriptions when module initializes', async () => {
    const subscribe = jest.fn();
    const operatorRecipientProvider: OperatorRecipientProvider = {
      resolveOperatorRecipientUserIds: jest.fn(),
    };
    const moduleRef = await Test.createTestingModule({
      providers: [
        ParcelSubscriptionOperatorEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        { provide: RedisService, useValue: { getClient: jest.fn() } },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
        { provide: OPERATOR_RECIPIENT_PROVIDER, useValue: operatorRecipientProvider },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS.length);

    await moduleRef.close();
  });
});

