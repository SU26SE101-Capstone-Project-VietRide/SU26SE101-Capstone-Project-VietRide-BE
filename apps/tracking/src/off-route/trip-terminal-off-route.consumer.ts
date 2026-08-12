import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { createHash, randomUUID } from 'node:crypto';
import pino from 'pino';
import { z, type ZodIssue } from 'zod';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import { OffRouteService } from './off-route.service';
import {
  TRIP_TERMINAL_OFF_ROUTE_CONSUMER_OPTIONS,
  TRIP_TERMINAL_OFF_ROUTE_PROCESSING_TTL_SECONDS,
  TRIP_TERMINAL_OFF_ROUTE_PROCESSED_TTL_SECONDS,
  TRIP_TERMINAL_OFF_ROUTE_QUEUE_BINDINGS,
} from './trip-terminal-off-route.constants';

const EVENT_ID_SCHEMA = z.string().uuid();
const SAFE_BROKER_IDENTIFIER = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;

const MARK_PROCESSED_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])
redis.call('DEL', KEYS[1])
return 1
`;

const RELEASE_LOCK_SCRIPT = `
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 0 end
return redis.call('DEL', KEYS[1])
`;

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
export class TripTerminalOffRouteConsumer implements OnModuleInit {
  private readonly logger = pino({ name: TripTerminalOffRouteConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly offRoute: OffRouteService,
    private readonly routeStateGeneration: RouteStateGenerationRegistry,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all(
      TRIP_TERMINAL_OFF_ROUTE_QUEUE_BINDINGS.map((binding) =>
        this.consumer.subscribe(
          binding.queue,
          binding.routingKey,
          (payload, raw) => this.handle(binding.routingKey, binding.schema, payload, raw),
          TRIP_TERMINAL_OFF_ROUTE_CONSUMER_OPTIONS,
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
    const client = this.redis.getClient();
    const processedKey = this.processedKey(messageIdentity);
    const processingKey = this.processingKey(messageIdentity);
    if (await client.get(processedKey)) return;

    const ownerToken = randomUUID();
    const acquired = await client.set(
      processingKey,
      ownerToken,
      'EX',
      TRIP_TERMINAL_OFF_ROUTE_PROCESSING_TTL_SECONDS,
      'NX',
    );
    if (acquired !== 'OK') throw new Error(`TRIP_TERMINAL_OFF_ROUTE_EVENT_LOCKED_${routingKey}`);

    try {
      if (await client.get(processedKey)) {
        await this.releaseOwnedLock(processingKey, ownerToken);
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
          'Dropping malformed terminal off-route event',
        );
        await this.requireMarkedProcessed(processingKey, processedKey, ownerToken);
        return;
      }

      this.routeStateGeneration.invalidate(parsed.data.tripId);
      await this.offRoute.clearRuntimeState(parsed.data.tripId);
      await this.requireMarkedProcessed(processingKey, processedKey, ownerToken);
      this.logger.info(
        { routingKey, messageIdentity, tripId: parsed.data.tripId },
        'Cleared off-route state after Trip termination',
      );
    } catch (error) {
      await this.releaseOwnedLock(processingKey, ownerToken);
      throw error;
    }
  }

  private async requireMarkedProcessed(
    processingKey: string,
    processedKey: string,
    ownerToken: string,
  ): Promise<void> {
    const result = await this.redis.getClient().eval(
      MARK_PROCESSED_SCRIPT,
      2,
      processingKey,
      processedKey,
      ownerToken,
      TRIP_TERMINAL_OFF_ROUTE_PROCESSED_TTL_SECONDS,
    );
    if (Number(result) !== 1) {
      throw new Error(`TRIP_TERMINAL_OFF_ROUTE_EVENT_LOCK_NOT_OWNED_${processedKey}`);
    }
  }

  private async releaseOwnedLock(processingKey: string, ownerToken: string): Promise<void> {
    await this.redis.getClient().eval(RELEASE_LOCK_SCRIPT, 1, processingKey, ownerToken);
  }

  private resolveMessageIdentity(payload: unknown, raw: ConsumeMessage): string {
    const payloadEventId = typeof payload === 'object' && payload !== null && 'eventId' in payload
      ? EVENT_ID_SCHEMA.safeParse(payload.eventId)
      : undefined;
    if (payloadEventId?.success) return payloadEventId.data.toLowerCase();
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

  private processedKey(messageIdentity: string): string {
    return `tracking:off_route_terminal:processed:${messageIdentity}`;
  }

  private processingKey(messageIdentity: string): string {
    return `tracking:off_route_terminal:processing:${messageIdentity}`;
  }
}
