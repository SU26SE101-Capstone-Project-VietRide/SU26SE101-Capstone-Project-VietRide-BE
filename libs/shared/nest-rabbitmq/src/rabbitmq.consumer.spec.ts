import type { ChannelModel, ConfirmChannel, ConsumeMessage } from 'amqplib';
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

  let channel: jest.Mocked<ConfirmChannel>;
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
      createConfirmChannel: jest.fn(async () => channel),
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

  it('acks only after the broker confirms the dlq publish', async () => {
    const consumer = createConsumer(connection);
    const handler: RabbitMqHandler = jest.fn(async () => {
      throw new Error('handler failed');
    });
    let confirmPublish: ((error: Error | null) => void) | undefined;
    channel.publish.mockImplementation((...args: unknown[]) => {
      confirmPublish = args.at(-1) as (error: Error | null) => void;
      return true;
    });

    await consumer.subscribe(queue, routingKey, handler, {
      deadLetter: true,
      maxRetries: 5,
    });

    const msg = createMessage({ rejectedCount: 5 });
    const handling = consumeHandler?.(msg);
    await Promise.resolve();

    expect(channel.nack).not.toHaveBeenCalled();
    expect(channel.publish).toHaveBeenCalledWith(
      dlqExchange,
      routingKey,
      msg.content,
      expect.objectContaining({
        contentType: 'application/json',
        persistent: true,
        messageId: 'message-1',
        correlationId: 'correlation-1',
      }),
      expect.any(Function),
    );
    expect(channel.ack).not.toHaveBeenCalled();

    confirmPublish?.(null);
    await handling;

    expect(channel.ack).toHaveBeenCalledWith(msg);
  });

  it('returns the original message to delayed retry when dlq publish confirm fails', async () => {
    const consumer = createConsumer(connection);
    const handler: RabbitMqHandler = jest.fn(async () => {
      throw new Error('handler failed');
    });
    channel.publish.mockImplementation((...args: unknown[]) => {
      const confirm = args.at(-1) as (error: Error | null) => void;
      confirm(new Error('broker rejected dlq publish'));
      return true;
    });

    await consumer.subscribe(queue, routingKey, handler, {
      deadLetter: true,
      maxRetries: 5,
    });

    const msg = createMessage({ rejectedCount: 5 });
    await consumeHandler?.(msg);

    expect(channel.ack).not.toHaveBeenCalled();
    expect(channel.nack).toHaveBeenCalledWith(msg, false, false);
  });

  it('registers a channel error listener so broker-closed channels do not crash the process', async () => {
    const consumer = createConsumer(connection);

    await consumer.subscribe(queue, routingKey, jest.fn());

    const errorListener = channel.on.mock.calls.find(([event]) => event === 'error')?.[1] as
      | ((err: Error) => void)
      | undefined;
    expect(errorListener).toBeDefined();
    expect(() => errorListener?.(new Error('channel closed by server'))).not.toThrow();
  });

  it('logs the conflicting queue and resolves when the broker rejects the queue declaration', async () => {
    const consumer = createConsumer(connection);
    const errorSpy = jest.spyOn(Logger.prototype, 'error');
    channel.assertQueue.mockRejectedValue(
      new Error(
        `PRECONDITION_FAILED - inequivalent arg 'x-dead-letter-routing-key' for queue '${queue}' in vhost '/'`,
      ),
    );

    await expect(
      consumer.subscribe(queue, routingKey, jest.fn(), { deadLetter: true }),
    ).resolves.toBeUndefined();

    expect(channel.consume).not.toHaveBeenCalled();
    expect(errorSpy).toHaveBeenCalledWith(
      expect.stringContaining(queue),
    );
    expect(errorSpy).toHaveBeenCalledWith(
      expect.stringContaining('PRECONDITION_FAILED'),
    );
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

function createChannelMock(): jest.Mocked<ConfirmChannel> {
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
    on: jest.fn(),
  } as unknown as jest.Mocked<ConfirmChannel>;
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
