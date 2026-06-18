import { Inject, Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import type { ChannelModel, ConfirmChannel } from 'amqplib';
import { RABBITMQ_CONNECTION, RABBITMQ_OPTIONS, type NestRabbitMqOptions } from './rabbitmq.tokens';

@Injectable()
export class RabbitMqPublisher implements OnModuleDestroy {
  private readonly logger = new Logger('RabbitMqPublisher');
  private channel?: ConfirmChannel;

  constructor(
    @Inject(RABBITMQ_CONNECTION) private readonly conn: ChannelModel,
    @Inject(RABBITMQ_OPTIONS) private readonly opts: NestRabbitMqOptions,
  ) {}

  private async getChannel(): Promise<ConfirmChannel> {
    if (this.channel) return this.channel;
    this.channel = await this.conn.createConfirmChannel();
    await this.channel.assertExchange(this.opts.exchange, this.opts.exchangeType ?? 'topic', {
      durable: true,
    });
    return this.channel;
  }

  /**
   * Publishes a JSON-serialised payload to the configured exchange with the
   * given routing key. Resolves once the broker confirms persistence.
   */
  async publish(routingKey: string, payload: unknown, headers: Record<string, string> = {}): Promise<void> {
    const ch = await this.getChannel();
    const body = Buffer.from(JSON.stringify(payload));
    const messageId = headers['eventId'];
    const correlationId = headers['correlationId'];
    await new Promise<void>((resolve, reject) => {
      ch.publish(
        this.opts.exchange,
        routingKey,
        body,
        {
          contentType: 'application/json',
          persistent: true,
          headers,
          ...(messageId ? { messageId } : {}),
          ...(correlationId ? { correlationId } : {}),
        },
        (err) => (err ? reject(err) : resolve()),
      );
    });
  }

  async onModuleDestroy(): Promise<void> {
    try {
      if (this.channel) await this.channel.close();
    } catch (err) {
      this.logger.warn(`Error closing channel: ${(err as Error).message}`);
    }
  }
}
