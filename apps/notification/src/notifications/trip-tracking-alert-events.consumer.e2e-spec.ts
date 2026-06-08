import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import { NotificationsService } from './notifications.service';
import { TRIP_TRACKING_ALERT_QUEUE_BINDINGS } from './trip-tracking-alert-events.constants';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

describe('TripTrackingAlertEventsConsumer registration (e2e)', () => {
  it('registers trip/tracking alert subscriptions when module initializes', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        TripTrackingAlertEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        { provide: RedisService, useValue: { getClient: jest.fn() } },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(TRIP_TRACKING_ALERT_QUEUE_BINDINGS.length);

    await moduleRef.close();
  });
});
