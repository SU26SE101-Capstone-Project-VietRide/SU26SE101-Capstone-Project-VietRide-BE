import { Test } from '@nestjs/testing';
import { MODULE_METADATA } from '@nestjs/common/constants';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { NotificationsModule } from './notifications.module';
import { NotificationsService } from './notifications.service';
import { MessageIdempotencyService } from './message-idempotency.service';
import {
  DAY24_DEPARTED_PENDING_QUEUE_BINDING,
  Day24DepartedPendingEventsConsumer,
} from './day24-departed-pending-events.consumer';
import {
  DAY24_NO_SHOW_QUEUE_BINDING,
  Day24NoShowEventsConsumer,
} from './day24-no-show-events.consumer';

describe('Day 24 stop/no-show notification e2e:', () => {
  it('registers both stop/no-show consumers in NotificationsModule', () => {
    const providers = Reflect.getMetadata(
      MODULE_METADATA.PROVIDERS,
      NotificationsModule,
    ) as unknown[];
    expect(providers).toEqual(
      expect.arrayContaining([Day24NoShowEventsConsumer, Day24DepartedPendingEventsConsumer]),
    );
  });

  it('binds the exact no-show and departed-pending keys', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        Day24NoShowEventsConsumer,
        Day24DepartedPendingEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        {
          provide: MessageIdempotencyService,
          useValue: { begin: jest.fn(), markProcessed: jest.fn(), release: jest.fn() },
        },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(2);
    expect(subscribe).toHaveBeenCalledWith(
      DAY24_NO_SHOW_QUEUE_BINDING.queue,
      DAY24_NO_SHOW_QUEUE_BINDING.routingKey,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
    expect(subscribe).toHaveBeenCalledWith(
      DAY24_DEPARTED_PENDING_QUEUE_BINDING.queue,
      DAY24_DEPARTED_PENDING_QUEUE_BINDING.routingKey,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );

    await moduleRef.close();
  });
});
