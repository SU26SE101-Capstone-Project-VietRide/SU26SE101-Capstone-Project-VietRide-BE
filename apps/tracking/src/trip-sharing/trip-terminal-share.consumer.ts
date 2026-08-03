import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { createHash } from 'node:crypto';
import pino from 'pino';
import { z, type ZodIssue } from 'zod';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import {
  TRIP_SHARE_TERMINAL_CONSUMER_OPTIONS,
  TRIP_SHARE_TERMINAL_QUEUE_BINDINGS,
} from './trip-terminal-share.constants';

const SAFE_BROKER_IDENTIFIER = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const EVENT_ID_SCHEMA = z.string().uuid();

interface ParsedTerminalEvent {
  eventId: string;
  tripId: string;
}

interface TerminalEventSchema {
  safeParse(payload: unknown):
    | { success: true; data: ParsedTerminalEvent }
    | { success: false; error: { issues: ZodIssue[] } };
}

@Injectable()
export class TripTerminalShareConsumer implements OnModuleInit {
  private readonly logger = pino({ name: TripTerminalShareConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: TripShareMessageIdempotencyRepository,
    private readonly grants: TripShareGrantRepository,
    private readonly realtime: TripShareRealtimePublisher,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      TRIP_SHARE_TERMINAL_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, binding.schema, payload, raw),
          TRIP_SHARE_TERMINAL_CONSUMER_OPTIONS,
        ),
      ),
    );
  }

  private async handle(
    routingKey: string,
    schema: TerminalEventSchema,
    payload: unknown,
    raw: ConsumeMessage,
  ): Promise<void> {
    const messageIdentity = this.resolveMessageIdentity(payload, raw);
    if (await this.idempotency.isProcessed(messageIdentity)) {
      this.logger.info({ routingKey, messageIdentity }, 'Skipping processed trip-share terminal event');
      return;
    }

    const ownerToken = await this.idempotency.acquire(messageIdentity);
    if (!ownerToken) throw new Error(`TRIP_SHARE_TERMINAL_EVENT_LOCKED_${routingKey}`);

    try {
      if (await this.idempotency.isProcessed(messageIdentity)) {
        await this.idempotency.release(messageIdentity, ownerToken);
        this.logger.info(
          { routingKey, messageIdentity },
          'Skipping trip-share terminal event completed before lock acquisition',
        );
        return;
      }

      const parsed = schema.safeParse(payload);
      if (!parsed.success) {
        this.logger.warn(
          {
            routingKey,
            messageIdentity,
            issues: parsed.error.issues.map((issue) => ({ code: issue.code, path: issue.path })),
          },
          'Dropping malformed trip-share terminal event',
        );
        await this.requireMarkedProcessed(messageIdentity, ownerToken);
        return;
      }

      await this.grants.revokeAllActiveForTrip(parsed.data.tripId, new Date());
      await this.realtime.revokeTrip(parsed.data.tripId, 'TRIP_ENDED');
      await this.requireMarkedProcessed(messageIdentity, ownerToken);
      this.logger.info(
        { routingKey, messageIdentity, tripId: parsed.data.tripId },
        'Processed trip-share terminal event',
      );
    } catch (error) {
      await this.idempotency.release(messageIdentity, ownerToken);
      throw error;
    }
  }

  private resolveMessageIdentity(payload: unknown, raw: ConsumeMessage): string {
    const eventId =
      typeof payload === 'object' && payload !== null && 'eventId' in payload
        ? EVENT_ID_SCHEMA.safeParse(payload.eventId)
        : undefined;
    if (eventId?.success) return eventId.data.toLowerCase();
    const messageId = this.readBrokerIdentifier(raw.properties?.messageId);
    if (messageId) return this.normalizeBrokerIdentifier(messageId);
    const correlationId = this.readBrokerIdentifier(raw.properties?.correlationId);
    if (correlationId) return this.normalizeBrokerIdentifier(correlationId);
    return createHash('sha256').update(raw.content).digest('hex');
  }

  private readBrokerIdentifier(value: unknown): string | undefined {
    return typeof value === 'string' && value.length > 0 ? value : undefined;
  }

  private normalizeBrokerIdentifier(value: string): string {
    if (SAFE_BROKER_IDENTIFIER.test(value)) return value;
    return createHash('sha256').update(value, 'utf8').digest('hex');
  }

  private async requireMarkedProcessed(messageIdentity: string, ownerToken: string): Promise<void> {
    if (await this.idempotency.markProcessed(messageIdentity, ownerToken)) return;
    throw new Error(`TRIP_SHARE_TERMINAL_EVENT_LOCK_NOT_OWNED_${messageIdentity}`);
  }
}
