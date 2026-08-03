import { Test } from '@nestjs/testing';
import {
  BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
  type BookingPendingActionAutoResolvedEvent,
} from '@vietride/contracts';
import { RabbitMqConsumer, RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import type { Channel, ChannelModel, ConsumeMessage } from 'amqplib';
import {
  BOOKING_TRIP_CHANGE_QUEUE_BINDINGS,
  BookingTripChangeEventsConsumer,
} from './booking-trip-change-events.consumer';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsService } from './notifications.service';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const AUTO_RESOLVED_QUEUE = 'notification:booking-pending-action-auto-resolved';

describe('Day 23 schedule notification e2e:', () => {
  it('registers durable/manual-ack subscriptions and ACKs redelivery without duplicating', async () => {
    const channels = new Map<string, jest.Mocked<Channel>>();
    const consumeHandlers = new Map<string, (message: ConsumeMessage | null) => Promise<void>>();
    const connection = {
      createConfirmChannel: jest.fn(async () => createChannel(channels, consumeHandlers)),
    } as unknown as jest.Mocked<ChannelModel>;
    const rabbitConsumer = new RabbitMqConsumer(
      connection,
      {
        url: 'amqp://localhost',
        exchange: 'vietride.events',
        exchangeType: 'topic',
      },
      new RabbitMqTopologyHealth(),
    );
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
    const moduleRef = await Test.createTestingModule({
      providers: [
        BookingTripChangeEventsConsumer,
        { provide: RabbitMqConsumer, useValue: rabbitConsumer },
        { provide: MessageIdempotencyService, useValue: idempotency },
        { provide: NotificationsService, useValue: notificationsService },
      ],
    }).compile();

    await moduleRef.init();

    expect(connection.createConfirmChannel).toHaveBeenCalledTimes(
      BOOKING_TRIP_CHANGE_QUEUE_BINDINGS.length,
    );
    const autoResolvedChannel = channels.get(AUTO_RESOLVED_QUEUE);
    expect(autoResolvedChannel).toBeDefined();
    expect(autoResolvedChannel?.assertExchange).toHaveBeenCalledWith('vietride.events', 'topic', {
      durable: true,
    });
    expect(autoResolvedChannel?.assertQueue).toHaveBeenCalledWith(
      AUTO_RESOLVED_QUEUE,
      expect.objectContaining({ durable: true }),
    );
    expect(autoResolvedChannel?.bindQueue).toHaveBeenCalledWith(
      AUTO_RESOLVED_QUEUE,
      'vietride.events',
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
    );
    expect(autoResolvedChannel?.prefetch).toHaveBeenCalledWith(1);

    const message = createMessage(autoResolvedPayload());
    const consume = consumeHandlers.get(AUTO_RESOLVED_QUEUE);
    expect(consume).toBeDefined();

    await consume?.(message);
    await consume?.(message);

    expect(notificationsService.createNotification).toHaveBeenCalledTimes(1);
    expect(idempotency.markProcessed).toHaveBeenCalledWith(
      BOOKING_PENDING_ACTION_AUTO_RESOLVED_ROUTING_KEY,
      EVENT_ID,
    );
    expect(autoResolvedChannel?.ack).toHaveBeenCalledTimes(2);
    expect(autoResolvedChannel?.ack).toHaveBeenNthCalledWith(1, message);
    expect(autoResolvedChannel?.ack).toHaveBeenNthCalledWith(2, message);
    expect(autoResolvedChannel?.nack).not.toHaveBeenCalled();

    await moduleRef.close();
  });
});

function createChannel(
  channels: Map<string, jest.Mocked<Channel>>,
  consumeHandlers: Map<string, (message: ConsumeMessage | null) => Promise<void>>,
): jest.Mocked<Channel> {
  const channel = {
    assertExchange: jest.fn(),
    assertQueue: jest.fn(),
    bindQueue: jest.fn(),
    prefetch: jest.fn(),
    consume: jest.fn(async (queue: string, handler: (message: ConsumeMessage | null) => void) => {
      channels.set(queue, channel as unknown as jest.Mocked<Channel>);
      consumeHandlers.set(queue, async (message) => handler(message));
      return { consumerTag: `consumer:${queue}` };
    }),
    ack: jest.fn(),
    nack: jest.fn(),
    publish: jest.fn(),
    close: jest.fn(),
    on: jest.fn(),
  } as unknown as jest.Mocked<Channel>;

  return channel;
}

function autoResolvedPayload(): BookingPendingActionAutoResolvedEvent {
  return {
    eventId: EVENT_ID,
    occurredAt: '2026-07-17T10:00:01+07:00',
    bookingId: '22222222-2222-4222-8222-222222222222',
    tripId: '33333333-3333-4333-8333-333333333333',
    userId: '44444444-4444-4444-8444-444444444444',
    pendingActionId: '55555555-5555-4555-8555-555555555555',
    resolvedAction: 'ACCEPTED',
    severity: 'MAJOR',
    oldDeparture: '2026-07-18T01:00:00+07:00',
    newDeparture: '2026-07-18T08:00:00+07:00',
  };
}

function createMessage(payload: BookingPendingActionAutoResolvedEvent): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(payload)),
    properties: {
      messageId: EVENT_ID,
      correlationId: undefined,
      headers: {},
      contentType: 'application/json',
    },
  } as ConsumeMessage;
}
