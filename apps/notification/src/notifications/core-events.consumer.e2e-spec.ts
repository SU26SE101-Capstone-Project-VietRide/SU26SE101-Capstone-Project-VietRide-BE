import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import { CoreEventsConsumer } from './core-events.consumer';
import { NotificationsService } from './notifications.service';

describe('CoreEventsConsumer registration (e2e)', () => {
  it('registers core event subscriptions when module initializes', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        CoreEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        { provide: RedisService, useValue: { getClient: jest.fn() } },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
      ],
    })
      .compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(5);

    await moduleRef.close();
  });
});
