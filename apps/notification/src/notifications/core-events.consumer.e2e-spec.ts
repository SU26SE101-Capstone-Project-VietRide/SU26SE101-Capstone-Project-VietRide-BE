import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { CoreEventsConsumer } from './core-events.consumer';
import { BOOKING_CANCELLED_ROUTING_KEY } from './core-events.constants';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

describe('CoreEventsConsumer registration (e2e)', () => {
  it('registers core event subscriptions when module initializes', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        CoreEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        { provide: MessageIdempotencyService, useValue: { begin: jest.fn(), markProcessed: jest.fn(), release: jest.fn() } },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
      ],
    })
      .compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(5);
    expect(subscribe).toHaveBeenCalledWith(
      'notification:booking-cancelled',
      BOOKING_CANCELLED_ROUTING_KEY,
      expect.any(Function),
      expect.any(Object),
    );
    expect(subscribe).not.toHaveBeenCalledWith(
      expect.any(String),
      'trip.trip.cancelled',
      expect.any(Function),
      expect.any(Object),
    );

    await moduleRef.close();
  });
});
