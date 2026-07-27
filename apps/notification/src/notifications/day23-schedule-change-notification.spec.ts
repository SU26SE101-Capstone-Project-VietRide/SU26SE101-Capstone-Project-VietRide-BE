import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
  BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
} from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { ZodError } from 'zod';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_TRIP_CHANGE_QUEUE_BINDINGS,
  BookingTripChangeEventsConsumer,
} from './booking-trip-change-events.consumer';
import { mapBookingTripChangeToNotification } from './booking-trip-change-notification.mapper';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';
import {
  TRIP_CREW_CHANGED_ROUTING_KEY,
  TRIP_ROUTE_CHANGED_ROUTING_KEY,
  TRIP_TRACKING_ALERT_QUEUE_BINDINGS,
} from './trip-tracking-alert-events.constants';

const REALERT_EVENT_ID = '11111111-1111-4111-8111-111111111111';
const AUTO_RESOLVED_EVENT_ID = '22222222-2222-4222-8222-222222222222';
const BOOKING_ID = '33333333-3333-4333-8333-333333333333';
const TRIP_ID = '44444444-4444-4444-8444-444444444444';
const USER_ID = '55555555-5555-4555-8555-555555555555';
const PENDING_ACTION_ID = '66666666-6666-4666-8666-666666666666';
const OLD_DEPARTURE = '2026-07-18T01:00:00+07:00';
const NEW_DEPARTURE = '2026-07-18T08:00:00+07:00';

const commonSchedule = {
  occurredAt: '2026-07-17T10:00:00+07:00',
  bookingId: BOOKING_ID,
  tripId: TRIP_ID,
  userId: USER_ID,
  pendingActionId: PENDING_ACTION_ID,
  oldDeparture: OLD_DEPARTURE,
  newDeparture: NEW_DEPARTURE,
};

describe('Day 23 schedule notification:', () => {
  it('keeps Booking passenger ownership and adds a separate crew-only Trip binding', () => {
    expect(BOOKING_TRIP_CHANGE_QUEUE_BINDINGS).toContainEqual({
      queue: 'notification:booking-pending-action-auto-resolved',
      routingKey: BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
    });

    const directTripRoutingKeys = TRIP_TRACKING_ALERT_QUEUE_BINDINGS.map(
      ({ routingKey }) => routingKey,
    );
    expect(directTripRoutingKeys).toContain('trip.trip.schedule_changed');
    expect(directTripRoutingKeys).not.toContain('trip.trip.cancelled');
    expect(directTripRoutingKeys).toContain(TRIP_ROUTE_CHANGED_ROUTING_KEY);
    expect(directTripRoutingKeys).toContain(TRIP_CREW_CHANGED_ROUTING_KEY);
  });

  it('mapper copies required, re-alerted, and auto-resolved schedule facts with existing type', () => {
    for (const severity of ['MEDIUM', 'MAJOR'] as const) {
      const required = mapBookingTripChangeToNotification(
        BOOKING_SCHEDULE_CHANGE_REQUIRED_ROUTING_KEY,
        {
          ...commonSchedule,
          eventId: REALERT_EVENT_ID,
          deadline: '2026-07-17T12:00:00+07:00',
          severity,
        },
      );
      const realerted = mapBookingTripChangeToNotification(
        BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
        {
          ...commonSchedule,
          eventId: REALERT_EVENT_ID,
          deadline: '2026-07-17T12:00:00+07:00',
          reason: 'SCHEDULE_CHANGE',
          severity,
        },
      );
      const autoResolved = mapBookingTripChangeToNotification(
        BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
        {
          ...commonSchedule,
          eventId: AUTO_RESOLVED_EVENT_ID,
          resolvedAction: 'ACCEPTED',
          severity,
        },
      );

      for (const notification of [required, realerted, autoResolved]) {
        expect(notification.userId).toBe(USER_ID);
        expect(notification.type).toBe(NotificationType.TRIP_SCHEDULE_CHANGED);
        expect(notification.data).toMatchObject({
          bookingId: BOOKING_ID,
          tripId: TRIP_ID,
          pendingActionId: PENDING_ACTION_ID,
          oldDeparture: OLD_DEPARTURE,
          newDeparture: NEW_DEPARTURE,
          severity,
        });
      }
      expect(autoResolved.data).toEqual(
        expect.objectContaining({ resolvedAction: 'ACCEPTED' }),
      );
    }

    expect(() =>
      mapBookingTripChangeToNotification(BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY, {
        ...commonSchedule,
        eventId: AUTO_RESOLVED_EVENT_ID,
        resolvedAction: 'ACCEPTED',
        severity: 'MINOR',
      }),
    ).toThrow(ZodError);
  });

  it('dedupe delivers distinct re-alert and auto-resolved phase MessageIds once each', async () => {
    const processed = new Set<string>();
    const idempotency = {
      begin: jest.fn(async (routingKey: string, messageId: string) =>
        processed.has(`${routingKey}:${messageId}`) ? 'duplicate' : 'acquired',
      ),
      markProcessed: jest.fn(async (routingKey: string, messageId: string) => {
        processed.add(`${routingKey}:${messageId}`);
      }),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    const notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    const consumer = new BookingTripChangeEventsConsumer(
      { subscribe: jest.fn() } as unknown as RabbitMqConsumer,
      idempotency,
      notificationsService,
    );
    const realertedPayload = {
      ...commonSchedule,
      eventId: REALERT_EVENT_ID,
      deadline: '2026-07-17T12:00:00+07:00',
      reason: 'SCHEDULE_CHANGE',
      severity: 'MAJOR',
    };
    const autoResolvedPayload = {
      ...commonSchedule,
      eventId: AUTO_RESOLVED_EVENT_ID,
      resolvedAction: 'ACCEPTED',
      severity: 'MAJOR',
    };

    await consumer.handle(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      realertedPayload,
      createMessage(REALERT_EVENT_ID),
    );
    await consumer.handle(
      BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY,
      realertedPayload,
      createMessage(REALERT_EVENT_ID),
    );
    await consumer.handle(
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
      autoResolvedPayload,
      createMessage(AUTO_RESOLVED_EVENT_ID),
    );
    await consumer.handle(
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
      autoResolvedPayload,
      createMessage(AUTO_RESOLVED_EVENT_ID),
    );

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(2);
    expect(notificationsService.createNotification).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({
        dedupeKey: `${BOOKING_PENDING_ACTION_REALERTED_ROUTING_KEY}:${REALERT_EVENT_ID}:${USER_ID}:${NotificationType.TRIP_SCHEDULE_CHANGED}`,
      }),
    );
    expect(notificationsService.createNotification).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        dedupeKey: `${BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY}:${AUTO_RESOLVED_EVENT_ID}:${USER_ID}:${NotificationType.TRIP_SCHEDULE_CHANGED}`,
      }),
    );
  });
});

function createMessage(messageId: string): ConsumeMessage {
  return { properties: { messageId, correlationId: undefined } } as ConsumeMessage;
}
