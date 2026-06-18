import type { Channel, ChannelModel, ConsumeMessage } from 'amqplib';
import { Logger } from '@nestjs/common';
import { RabbitMqConsumer, type RabbitMqHandler } from './rabbitmq.consumer';
import type { NestRabbitMqOptions } from './rabbitmq.tokens';

describe('RabbitMqConsumer', () => {
  const exchange = 'vietride.events';
  const queue = 'notification.trip.delayed';
  const routingKey = 'trip.trip.delayed';
  const retryExchange = `${exchange}.retry`;
  const dlqExchange = `${exchange}.dlq`;
  const retryReturnRoutingKey = `__retry__.${queue}`;

  let channel: jest.Mocked<Channel>;
  let connection: jest.Mocked<ChannelModel>;
  let consumeHandler: ((msg: ConsumeMessage | null) => Promise<void>) | undefined;

  beforeEach(() => {
    jest.spyOn(Logger.prototype, 'error').mockImplementation(() => undefined);
    jest.spyOn(Logger.prototype, 'warn').mockImplementation(() => undefined);
    consumeHandler = undefined;
    channel = createChannelMock();
    channel.consume.mockImplementation(async (_queue, onMessage) => {
      consumeHandler = onMessage as (msg: ConsumeMessage | null) => Promise<void>;
      return { consumerTag: 'consumer-1' };
    });
    connection = {
      createChannel: jest.fn(async () => channel),
    } as unknown as jest.Mocked<ChannelModel>;
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('asserts delayed retry and dlq topology without returning retries via the public routing key', async () => {
    const consumer = createConsumer(connection);

    await consumer.subscribe(queue, routingKey, jest.fn(), {
      prefetch: 1,
      deadLetter: true,
      maxRetries: 5,
      retryDelayMs: 12_000,
    });

    expect(channel.assertExchange).toHaveBeenCalledWith(exchange, 'topic', { durable: true });
    expect(channel.assertExchange).toHaveBeenCalledWith(retryExchange, 'topic', { durable: true });
    expect(channel.assertExchange).toHaveBeenCalledWith(dlqExchange, 'topic', { durable: true });

    expect(channel.assertQueue).toHaveBeenCalledWith(`${queue}.retry`, {
      durable: true,
      arguments: {
        'x-message-ttl': 12_000,
        'x-dead-letter-exchange': exchange,
        'x-dead-letter-routing-key': retryReturnRoutingKey,
      },
    });
    expect(channel.bindQueue).toHaveBeenCalledWith(`${queue}.retry`, retryExchange, routingKey);

    expect(channel.assertQueue).toHaveBeenCalledWith(queue, {
      durable: true,
      arguments: {
        'x-dead-letter-exchange': retryExchange,
        'x-dead-letter-routing-key': routingKey,
      },
    });
    expect(channel.bindQueue).toHaveBeenCalledWith(queue, exchange, routingKey);
    expect(channel.bindQueue).toHaveBeenCalledWith(queue, exchange, retryReturnRoutingKey);

    expect(channel.assertQueue).toHaveBeenCalledWith(`${queue}.dlq`, { durable: true });
    expect(channel.bindQueue).toHaveBeenCalledWith(`${queue}.dlq`, dlqExchange, routingKey);
    expect(channel.prefetch).toHaveBeenCalledWith(1);
  });

  it('nacks failed messages without requeue until max retry count is reached', async () => {
    const consumer = createConsumer(connection);
    const handler: RabbitMqHandler = jest.fn(async () => {
      throw new Error('handler failed');
    });

    await consumer.subscribe(queue, routingKey, handler, {
      deadLetter: true,
      maxRetries: 5,
    });

    await consumeHandler?.(createMessage({ rejectedCount: 4 }));

    expect(channel.nack).toHaveBeenCalledWith(expect.any(Object), false, false);
    expect(channel.publish).not.toHaveBeenCalled();
    expect(channel.ack).not.toHaveBeenCalled();
  });

  it('parks failed messages in the dlq exchange once max retry count is reached', async () => {
    const consumer = createConsumer(connection);
    const handler: RabbitMqHandler = jest.fn(async () => {
      throw new Error('handler failed');
    });

    await consumer.subscribe(queue, routingKey, handler, {
      deadLetter: true,
      maxRetries: 5,
    });

    const msg = createMessage({ rejectedCount: 5 });
    await consumeHandler?.(msg);

    expect(channel.nack).not.toHaveBeenCalled();
    expect(channel.publish).toHaveBeenCalledWith(dlqExchange, routingKey, msg.content, {
      contentType: 'application/json',
      persistent: true,
      headers: {
        traceId: 'trace-1',
        'x-death': [
          { queue, reason: 'rejected', count: 5 },
          { queue, reason: 'expired', count: 5 },
          { queue: 'other.queue', reason: 'rejected', count: 99 },
        ],
        'x-vietride-dlq-reason': 'max-retries-exceeded',
        'x-vietride-retry-count': 5,
      },
      messageId: 'message-1',
      correlationId: 'correlation-1',
    });
    expect(channel.ack).toHaveBeenCalledWith(msg);
  });

  it('acks messages after successful json parse and handler completion', async () => {
    const consumer = createConsumer(connection);
    const handler: RabbitMqHandler<{ hello: string }> = jest.fn();

    await consumer.subscribe(queue, routingKey, handler);

    const msg = createMessage({ content: { hello: 'world' } });
    await consumeHandler?.(msg);

    expect(handler).toHaveBeenCalledWith({ hello: 'world' }, msg);
    expect(channel.ack).toHaveBeenCalledWith(msg);
    expect(channel.nack).not.toHaveBeenCalled();
  });
});

function createConsumer(connection: ChannelModel): RabbitMqConsumer {
  const options: NestRabbitMqOptions = {
    url: 'amqp://localhost',
    exchange: 'vietride.events',
    exchangeType: 'topic',
  };

  return new RabbitMqConsumer(connection, options);
}

function createChannelMock(): jest.Mocked<Channel> {
  return {
    assertExchange: jest.fn(),
    assertQueue: jest.fn(),
    bindQueue: jest.fn(),
    prefetch: jest.fn(),
    consume: jest.fn(),
    ack: jest.fn(),
    nack: jest.fn(),
    publish: jest.fn(),
    close: jest.fn(),
  } as unknown as jest.Mocked<Channel>;
}

function createMessage({
  content = { ok: true },
  rejectedCount = 0,
}: {
  content?: unknown;
  rejectedCount?: number;
} = {}): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(content)),
    properties: {
      contentType: 'application/json',
      headers: {
        traceId: 'trace-1',
        'x-death': [
          { queue: 'notification.trip.delayed', reason: 'rejected', count: rejectedCount },
          { queue: 'notification.trip.delayed', reason: 'expired', count: rejectedCount },
          { queue: 'other.queue', reason: 'rejected', count: 99 },
        ],
      },
      messageId: 'message-1',
      correlationId: 'correlation-1',
    },
  } as unknown as ConsumeMessage;
}
