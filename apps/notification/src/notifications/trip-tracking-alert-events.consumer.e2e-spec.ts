import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import { TRIP_TRACKING_ALERT_QUEUE_BINDINGS } from './trip-tracking-alert-events.constants';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

describe('TripTrackingAlertEventsConsumer registration (e2e)', () => {
  it('registers trip/tracking alert subscriptions when module initializes', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        TripTrackingAlertEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        {
          provide: MessageIdempotencyService,
          useValue: { begin: jest.fn(), markProcessed: jest.fn(), release: jest.fn() },
        },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
        {
          provide: OPERATOR_RECIPIENT_PROVIDER,
          useValue: { resolveOperatorRecipientUserIds: jest.fn() },
        },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(TRIP_TRACKING_ALERT_QUEUE_BINDINGS.length);
    expect(subscribe).not.toHaveBeenCalledWith(
      expect.any(String),
      'trip.trip.schedule_changed',
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
