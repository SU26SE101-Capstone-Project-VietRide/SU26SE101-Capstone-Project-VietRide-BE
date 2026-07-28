import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { NotificationType } from '../generated/notification-prisma-client';
import {
  BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
  type BookingPassengerNoShowMarkedEvent,
} from '@vietride/contracts';
import {
  DAY24_NO_SHOW_QUEUE_BINDING,
  Day24NoShowEventsConsumer,
  mapPassengerNoShowToNotification,
} from './day24-no-show-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const BOOKING_ID = '22222222-2222-4222-8222-222222222222';
const TRIP_ID = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const PASSENGER_ID = '55555555-5555-4555-8555-555555555555';

describe('Day 24 no-show notification:', () => {
  let rabbitConsumer: jest.Mocked<RabbitMqConsumer>;
  let idempotency: jest.Mocked<MessageIdempotencyService>;
  let notificationsService: jest.Mocked<NotificationsService>;
  let consumer: Day24NoShowEventsConsumer;

  beforeEach(() => {
    rabbitConsumer = { subscribe: jest.fn() } as unknown as jest.Mocked<RabbitMqConsumer>;
    idempotency = {
      begin: jest.fn(),
      markProcessed: jest.fn(),
      release: jest.fn(),
    } as unknown as jest.Mocked<MessageIdempotencyService>;
    notificationsService = {
      createNotification: jest.fn(),
    } as unknown as jest.Mocked<NotificationsService>;
    consumer = new Day24NoShowEventsConsumer(rabbitConsumer, idempotency, notificationsService);
  });

  it('maps PASSENGER_NO_SHOW to the booking user only', () => {
    expect(mapPassengerNoShowToNotification(canonicalPayload())).toEqual({
      userId: USER_ID,
      type: NotificationType.PASSENGER_NO_SHOW,
      title: 'Bạn đã lỡ chuyến xe',
      body: 'Bạn đã không lên xe đúng giờ. Vé không được hoàn tiền theo chính sách.',
      data: {
        eventId: EVENT_ID,
        occurredAt: '2026-07-18T10:00:00+07:00',
        eventType: BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
        bookingId: BOOKING_ID,
        tripId: TRIP_ID,
        bookingStatus: 'NO_SHOW',
        newlyNoShowPassengerIds: [PASSENGER_ID],
        triggerType: 'TERMINAL',
        pickupStopId: null,
      },
    });
  });

  it('dedupes EventId redelivery and does not notify a second recipient', async () => {
    idempotency.begin.mockResolvedValueOnce('acquired').mockResolvedValueOnce('duplicate');

    await consumer.handle(canonicalPayload(), createMessage(EVENT_ID));
    await consumer.handle(canonicalPayload(), createMessage(EVENT_ID));

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(notificationsService.createNotification).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: USER_ID,
        type: NotificationType.PASSENGER_NO_SHOW,
        dedupeKey: `${BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY}:${EVENT_ID}:${USER_ID}:${NotificationType.PASSENGER_NO_SHOW}`,
      }),
    );
  });

  it('drops a malformed trigger shape according to the consumer policy', async () => {
    idempotency.begin.mockResolvedValue('acquired');
    await consumer.handle(
      { ...canonicalPayload(), triggerType: 'ALONG_ROUTE' },
      createMessage(EVENT_ID),
    );

    expect(notificationsService.createNotification).not.toHaveBeenCalled();
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
      EVENT_ID,
    );
  });
});

function canonicalPayload(): BookingPassengerNoShowMarkedEvent {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-18T10:00:00+07:00',
    eventType: BOOKING_PASSENGER_NO_SHOW_MARKED_ROUTING_KEY,
    bookingId: BOOKING_ID,
    tripId: TRIP_ID,
    userId: USER_ID,
    bookingStatus: 'NO_SHOW' as const,
    newlyNoShowPassengerIds: [PASSENGER_ID],
    triggerType: 'TERMINAL' as const,
  };
}

function createMessage(messageId: string): ConsumeMessage {
  return { properties: { messageId, correlationId: undefined } } as ConsumeMessage;
}

void DAY24_NO_SHOW_QUEUE_BINDING;
