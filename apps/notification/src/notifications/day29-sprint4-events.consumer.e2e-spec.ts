import { Test } from '@nestjs/testing';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';
import { PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS } from './parcel-subscription-operator-events.constants';
import { TRIP_TRACKING_ALERT_QUEUE_BINDINGS } from './trip-tracking-alert-events.constants';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

describe('Day 29 Sprint 4 notification subscriptions (e2e)', () => {
  it('registers every Day 29 Sprint 4 Trip and Parcel routing key', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        TripTrackingAlertEventsConsumer,
        ParcelSubscriptionOperatorEventsConsumer,
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

    const routingKeys = subscribe.mock.calls.map(([, routingKey]) => routingKey);
    for (const binding of [
      ...TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
      ...PARCEL_SUBSCRIPTION_OPERATOR_QUEUE_BINDINGS,
    ]) {
      expect(routingKeys).toContain(binding.routingKey);
    }
    expect(routingKeys).toContain('trip.trip.boarding_started');
    expect(routingKeys).toContain('trip.cargo.threshold_crossed');
    expect(routingKeys).toContain('parcel.parcel.created');
    expect(routingKeys).toContain('parcel.parcel.loaded');
    expect(routingKeys).toContain('parcel.parcel.unloaded');
    expect(routingKeys).toContain('parcel.parcel.review_requested');
    expect(routingKeys).toContain('parcel.parcel.auto_rejected');
    expect(routingKeys).not.toContain(`trip.cargo_${'near_full'}`);

    await moduleRef.close();
  });
});
