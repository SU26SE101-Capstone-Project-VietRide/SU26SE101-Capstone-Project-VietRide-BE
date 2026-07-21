import { Injectable } from '@nestjs/common';
import { RabbitMqPublisher } from '@vietride/nest-rabbitmq';
import pino from 'pino';
import { OUTBOX_EVENT_ROUTING_KEYS } from './outbox.constants';
import type { OutboxEventRecord } from './outbox.repository';
import { OutboxRepository } from './outbox.repository';

@Injectable()
export class OutboxPublisherService {
  private readonly logger = pino({ name: OutboxPublisherService.name });

  constructor(
    private readonly repository: OutboxRepository,
    private readonly publisher: RabbitMqPublisher,
  ) {}

  async publishPendingOnce(limit: number): Promise<number> {
    const recovered = await this.repository.recoverStalePublishingEvents();
    if (recovered > 0) {
      this.logger.warn({ recovered }, 'Recovered stale PUBLISHING outbox events');
    }

    const events = await this.repository.findPublishable(limit);
    let published = 0;

    for (const event of events) {
      const didPublish = await this.publishEvent(event);
      if (didPublish) published += 1;
    }

    return published;
  }

  private async publishEvent(event: OutboxEventRecord): Promise<boolean> {
    const locked = await this.repository.markPublishing(event.id);
    if (!locked) return false;

    try {
      const routingKey = resolveRoutingKey(event.eventType);
      assertPublishablePayload(event.payload);

      await this.publisher.publish(routingKey, event.payload, {
        eventId: event.id,
        eventType: event.eventType,
      });
      return this.repository.markPublished(event.id, new Date());
    } catch (error) {
      await this.repository.markFailed(event.id, error);
      this.logger.warn(
        { err: error, eventId: event.id, eventType: event.eventType },
        'Outbox event publish failed',
      );
      return false;
    }
  }
}

function resolveRoutingKey(eventType: string): string {
  const routingKey = OUTBOX_EVENT_ROUTING_KEYS[eventType];
  if (!routingKey) {
    throw new Error(`Unsupported outbox event type: ${eventType}`);
  }

  return routingKey;
}

function assertPublishablePayload(payload: unknown): asserts payload is Record<string, unknown> {
  if (typeof payload !== 'object' || payload === null || Array.isArray(payload)) {
    throw new Error('Malformed outbox payload');
  }
}
