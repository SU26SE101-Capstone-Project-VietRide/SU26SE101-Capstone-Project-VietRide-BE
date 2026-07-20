import { Test } from '@nestjs/testing';
import { MODULE_METADATA } from '@nestjs/common/constants';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { NotificationsModule } from './notifications.module';
import { NotificationsService } from './notifications.service';
import { MessageIdempotencyService } from './message-idempotency.service';
import {
  DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING,
  Day24StopDisabledAutoFallbackEventsConsumer,
} from './day24-stop-disabled-auto-fallback-events.consumer';

describe('Day 24 fallback notification e2e:', () => {
  it('registers the auto-fallback consumer in NotificationsModule', () => {
    const providers = Reflect.getMetadata(
      MODULE_METADATA.PROVIDERS,
      NotificationsModule,
    ) as unknown[];
    expect(providers).toContain(Day24StopDisabledAutoFallbackEventsConsumer);
  });

  it('binds only the Booking auto-fallback routing key', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        Day24StopDisabledAutoFallbackEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        {
          provide: MessageIdempotencyService,
          useValue: { begin: jest.fn(), markProcessed: jest.fn(), release: jest.fn() },
        },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(1);
    expect(subscribe).toHaveBeenCalledWith(
      DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING.queue,
      DAY24_STOP_DISABLED_AUTO_FALLBACK_QUEUE_BINDING.routingKey,
      expect.any(Function),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
    expect(subscribe).not.toHaveBeenCalledWith(
      expect.any(String),
      'booking.booking.pending_action_auto_resolved',
      expect.any(Function),
      expect.any(Object),
    );

    await moduleRef.close();
  });
});
