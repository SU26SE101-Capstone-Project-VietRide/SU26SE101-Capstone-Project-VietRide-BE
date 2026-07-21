import { ZodError } from 'zod';
import type { ConsumeMessage } from 'amqplib';
import type { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { NotificationType } from '../generated/notification-prisma-client';
import { mapCoreEventToNotification } from './core-event-notification.mapper';
import { BOOKING_CANCELLED_ROUTING_KEY } from './core-events.constants';
import { CoreEventsConsumer } from './core-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const event = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-07-17T00:00:00+00:00',
  bookingId: '22222222-2222-4222-8222-222222222222',
  userId: '33333333-3333-4333-8333-333333333333',
  refundAmount: 0,
  refundOverride: false,
  cancellationReason: 'USER_INITIATED',
};

describe('Day 23 booking.cancelled compatibility:', () => {
  it('maps canonical cancellation identity without weakening the payload', () => {
    expect(mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, event)).toEqual(
      expect.objectContaining({ userId: event.userId, type: NotificationType.BOOKING_CANCELLED }),
    );
  });

  it('accepts only exact legacy omission and rejects partial identity', () => {
    const { eventId, occurredAt, ...legacy } = event;
    expect(eventId).toBe(event.eventId);
    expect(occurredAt).toBe(event.occurredAt);
    expect(() => mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, legacy)).not.toThrow();
    expect(() =>
      mapCoreEventToNotification(BOOKING_CANCELLED_ROUTING_KEY, { ...legacy, eventId: event.eventId }),
    ).toThrow(ZodError);
  });

  it('dedupes canonical redelivery by payload event identity when the broker id differs', async () => {
    const idempotency = {
      begin: jest.fn().mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate'),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    const notifications = {
      createNotification: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<NotificationsService>;
    const consumer = new CoreEventsConsumer(
      {} as RabbitMqConsumer,
      idempotency,
      notifications,
    );
    const raw = { properties: { messageId: 'broker-canonical-id' } } as ConsumeMessage;

    await consumer.handle(BOOKING_CANCELLED_ROUTING_KEY, event, raw);
    await consumer.handle(BOOKING_CANCELLED_ROUTING_KEY, event, raw);

    expect(notifications.createNotification).toHaveBeenCalledTimes(1);
    expect(idempotency.begin).toHaveBeenCalledTimes(2);
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      1,
      BOOKING_CANCELLED_ROUTING_KEY,
      event.eventId,
    );
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      2,
      BOOKING_CANCELLED_ROUTING_KEY,
      event.eventId,
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        dedupeKey: `${BOOKING_CANCELLED_ROUTING_KEY}:${event.eventId}:${event.userId}:${NotificationType.BOOKING_CANCELLED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_CANCELLED_ROUTING_KEY,
      event.eventId,
    );
  });

  it('dedupes exact legacy redelivery by booking identity when the broker id differs', async () => {
    const { eventId, occurredAt, ...legacy } = event;
    const idempotency = {
      begin: jest.fn().mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate'),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    const notifications = {
      createNotification: jest.fn().mockResolvedValue(undefined),
    } as unknown as jest.Mocked<NotificationsService>;
    const consumer = new CoreEventsConsumer(
      {} as RabbitMqConsumer,
      idempotency,
      notifications,
    );
    const raw = { properties: { messageId: 'broker-legacy-id' } } as ConsumeMessage;

    await consumer.handle(BOOKING_CANCELLED_ROUTING_KEY, legacy, raw);
    await consumer.handle(BOOKING_CANCELLED_ROUTING_KEY, legacy, raw);

    expect(eventId).toBe(event.eventId);
    expect(occurredAt).toBe(event.occurredAt);
    expect(notifications.createNotification).toHaveBeenCalledTimes(1);
    expect(idempotency.begin).toHaveBeenCalledTimes(2);
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      1,
      BOOKING_CANCELLED_ROUTING_KEY,
      event.bookingId,
    );
    expect(idempotency.begin).toHaveBeenNthCalledWith(
      2,
      BOOKING_CANCELLED_ROUTING_KEY,
      event.bookingId,
    );
    expect(notifications.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        dedupeKey: `${BOOKING_CANCELLED_ROUTING_KEY}:${event.bookingId}:${event.userId}:${NotificationType.BOOKING_CANCELLED}`,
      }),
    );
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_CANCELLED_ROUTING_KEY,
      event.bookingId,
    );
  });
});
