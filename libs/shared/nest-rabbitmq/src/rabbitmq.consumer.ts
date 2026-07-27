import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import type { ChannelModel, ConfirmChannel, ConsumeMessage } from 'amqplib';
import { RABBITMQ_CONNECTION, RABBITMQ_OPTIONS, type NestRabbitMqOptions } from './rabbitmq.tokens';

export type RabbitMqHandler<T = unknown> = (payload: T, raw: ConsumeMessage) => Promise<void> | void;

export interface RabbitMqSubscribeOptions {
  prefetch?: number;
  /**
   * @deprecated Use deadLetter + maxRetries + retryDelayMs. Kept for callers that
   * have not moved to the delayed retry topology yet.
   */
  requeueOnError?: boolean;
  deadLetter?: boolean;
  maxRetries?: number;
  retryDelayMs?: number;
}

const DEFAULT_MAX_RETRIES = 5;
const DEFAULT_RETRY_DELAY_MS = 10_000;

@Injectable()
export class RabbitMqConsumer implements OnModuleDestroy {
  private readonly logger = new Logger('RabbitMqConsumer');
  private readonly channels: ConfirmChannel[] = [];

  constructor(
    @Inject(RABBITMQ_CONNECTION) private readonly conn: ChannelModel,
    @Inject(RABBITMQ_OPTIONS) private readonly opts: NestRabbitMqOptions,
  ) {}

  /**
   * Asserts `queue`, binds it to the configured exchange under `routingKey`,
   * and consumes messages, parsing JSON and dispatching to `handler`.
   * Ack on success, nack (no-requeue) on handler throw.
   */
  async subscribe<T = unknown>(
    queue: string,
    routingKey: string,
    handler: RabbitMqHandler<T>,
    options: RabbitMqSubscribeOptions = {},
  ): Promise<void> {
    const ch = await this.conn.createConfirmChannel();
    await ch.assertExchange(this.opts.exchange, this.opts.exchangeType ?? 'topic', { durable: true });
    if (options.deadLetter) {
      await this.assertRetryTopology(ch, queue, routingKey, options.retryDelayMs ?? DEFAULT_RETRY_DELAY_MS);
    }

    await ch.assertQueue(queue, {
      durable: true,
      ...(options.deadLetter
        ? {
            arguments: {
              'x-dead-letter-exchange': this.retryExchangeName(),
              'x-dead-letter-routing-key': routingKey,
            },
          }
        : {}),
    });
    await ch.bindQueue(queue, this.opts.exchange, routingKey);
    if (options.deadLetter) {
      await ch.bindQueue(queue, this.opts.exchange, this.retryReturnRoutingKey(queue));
    }
    if (options.prefetch) await ch.prefetch(options.prefetch);

    await ch.consume(queue, async (msg) => {
      if (!msg) return;
      try {
        const payload = JSON.parse(msg.content.toString('utf8')) as T;
        await handler(payload, msg);
        ch.ack(msg);
      } catch (err) {
        this.logger.error(`Handler failed on queue=${queue} rk=${routingKey}: ${(err as Error).message}`);
        if (options.deadLetter) {
          try {
            await this.retryOrPark(
              ch,
              queue,
              routingKey,
              msg,
              options.maxRetries ?? DEFAULT_MAX_RETRIES,
            );
          } catch (publishError) {
            this.logger.error(
              `DLQ publish confirm failed on queue=${queue} rk=${routingKey}: ${(publishError as Error).message}`,
            );
            ch.nack(msg, false, false);
          }
          return;
        }

        ch.nack(msg, false, options.requeueOnError ?? false);
      }
    });

    this.channels.push(ch);
  }

  private async assertRetryTopology(
    ch: ConfirmChannel,
    queue: string,
    routingKey: string,
    retryDelayMs: number,
  ): Promise<void> {
    const retryExchange = this.retryExchangeName();
    const dlqExchange = this.dlqExchangeName();
    const retryQueue = `${queue}.retry`;
    const dlqQueue = `${queue}.dlq`;
    const retryReturnRoutingKey = this.retryReturnRoutingKey(queue);

    await ch.assertExchange(retryExchange, this.opts.exchangeType ?? 'topic', { durable: true });
    await ch.assertExchange(dlqExchange, this.opts.exchangeType ?? 'topic', { durable: true });
    await ch.assertQueue(retryQueue, {
      durable: true,
      arguments: {
        'x-message-ttl': retryDelayMs,
        'x-dead-letter-exchange': this.opts.exchange,
        'x-dead-letter-routing-key': retryReturnRoutingKey,
      },
    });
    await ch.bindQueue(retryQueue, retryExchange, routingKey);
    await ch.assertQueue(dlqQueue, { durable: true });
    await ch.bindQueue(dlqQueue, dlqExchange, routingKey);
  }

  private async retryOrPark(
    ch: ConfirmChannel,
    queue: string,
    routingKey: string,
    msg: ConsumeMessage,
    maxRetries: number,
  ): Promise<void> {
    const rejectedCount = readXDeathCount(msg, queue, 'rejected');
    if (rejectedCount < maxRetries) {
      ch.nack(msg, false, false);
      return;
    }

    const headers = {
      ...(msg.properties.headers ?? {}),
      'x-vietride-dlq-reason': 'max-retries-exceeded',
      'x-vietride-retry-count': rejectedCount,
    };

    await new Promise<void>((resolve, reject) => {
      ch.publish(
        this.dlqExchangeName(),
        routingKey,
        msg.content,
        {
          contentType: msg.properties.contentType,
          persistent: true,
          headers,
          messageId: msg.properties.messageId,
          correlationId: msg.properties.correlationId,
        },
        (error) => (error ? reject(error) : resolve()),
      );
    });
    ch.ack(msg);
    this.logger.warn(`Parked message on queue=${queue} rk=${routingKey} after retries=${rejectedCount}`);
  }

  private retryExchangeName(): string {
    return `${this.opts.exchange}.retry`;
  }

  private dlqExchangeName(): string {
    return `${this.opts.exchange}.dlq`;
  }

  private retryReturnRoutingKey(queue: string): string {
    return `__retry__.${queue}`;
  }

  async onModuleDestroy(): Promise<void> {
    for (const ch of this.channels) {
      try {
        await ch.close();
      } catch (err) {
        this.logger.warn(`Error closing channel: ${(err as Error).message}`);
      }
    }
  }
}

function readXDeathCount(msg: ConsumeMessage, queue: string, reason: string): number {
  const xDeath = msg.properties.headers?.['x-death'];
  if (!Array.isArray(xDeath)) return 0;

  return xDeath
    .filter((entry) => entry !== null && typeof entry === 'object')
    .map((entry) => entry as unknown as Record<string, unknown>)
    .filter((entry) => entry['queue'] === queue && entry['reason'] === reason)
    .reduce((total, entry) => {
      const count = entry['count'];
      return total + (typeof count === 'number' ? count : 0);
    }, 0);
}
