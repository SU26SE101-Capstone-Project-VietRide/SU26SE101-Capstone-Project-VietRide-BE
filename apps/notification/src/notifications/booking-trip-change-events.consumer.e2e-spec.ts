import { Test } from '@nestjs/testing';
import { MODULE_METADATA } from '@nestjs/common/constants';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import {
  BOOKING_TRIP_CHANGE_QUEUE_BINDINGS,
  BookingTripChangeEventsConsumer,
} from './booking-trip-change-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import { NotificationsModule } from './notifications.module';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';

describe('BookingTripChangeEventsConsumer registers the Booking-owned passenger subscriptions (e2e)', () => {
  it('is registered by NotificationsModule', () => {
    const providers = Reflect.getMetadata(
      MODULE_METADATA.PROVIDERS,
      NotificationsModule,
    ) as unknown[];

    expect(providers).toContain(BookingTripChangeEventsConsumer);
  });

  it('registers the Booking-owned passenger subscriptions', async () => {
    const subscribe = jest.fn();
    const moduleRef = await Test.createTestingModule({
      providers: [
        BookingTripChangeEventsConsumer,
        { provide: RabbitMqConsumer, useValue: { subscribe } },
        {
          provide: MessageIdempotencyService,
          useValue: { begin: jest.fn(), markProcessed: jest.fn(), release: jest.fn() },
        },
        { provide: NotificationsService, useValue: { createNotification: jest.fn() } },
        {
          provide: OPERATOR_RECIPIENT_PROVIDER,
          useValue: { resolveOperatorRecipientUserIds: jest.fn().mockResolvedValue([]) },
        },
      ],
    }).compile();

    await moduleRef.init();

    expect(subscribe).toHaveBeenCalledTimes(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.length);
    for (const binding of BOOKING_TRIP_CHANGE_QUEUE_BINDINGS) {
      expect(subscribe).toHaveBeenCalledWith(
        binding.queue,
        binding.routingKey,
        expect.any(Function),
        { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
      );
    }
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
    expect(subscribe).not.toHaveBeenCalledWith(
      expect.any(String),
      'booking.booking.stop_disabled_auto_fallback_applied',
      expect.any(Function),
      expect.any(Object),
    );
    expect(subscribe).not.toHaveBeenCalledWith(
      expect.any(String),
      'booking.booking.passenger_no_show_marked',
      expect.any(Function),
      expect.any(Object),
    );

    await moduleRef.close();
  });
});
