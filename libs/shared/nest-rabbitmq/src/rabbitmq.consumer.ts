import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import type { Channel, ChannelModel, ConsumeMessage } from 'amqplib';
import { RABBITMQ_CONNECTION, RABBITMQ_OPTIONS, type NestRabbitMqOptions } from './rabbitmq.tokens';

export type RabbitMqHandler<T = unknown> = (payload: T, raw: ConsumeMessage) => Promise<void> | void;

@Injectable()
export class RabbitMqConsumer implements OnModuleDestroy {
  private readonly logger = new Logger('RabbitMqConsumer');
  private readonly channels: Channel[] = [];

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
    options: { prefetch?: number } = {},
  ): Promise<void> {
    const ch = await this.conn.createChannel();
    await ch.assertExchange(this.opts.exchange, this.opts.exchangeType ?? 'topic', { durable: true });
    await ch.assertQueue(queue, { durable: true });
    await ch.bindQueue(queue, this.opts.exchange, routingKey);
    if (options.prefetch) await ch.prefetch(options.prefetch);

    await ch.consume(queue, async (msg) => {
      if (!msg) return;
      try {
        const payload = JSON.parse(msg.content.toString('utf8')) as T;
        await handler(payload, msg);
        ch.ack(msg);
      } catch (err) {
        this.logger.error(`Handler failed on queue=${queue} rk=${routingKey}: ${(err as Error).message}`);
        ch.nack(msg, false, false);
      }
    });

    this.channels.push(ch);
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
