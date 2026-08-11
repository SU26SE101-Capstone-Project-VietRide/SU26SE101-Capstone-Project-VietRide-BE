import { Inject, Injectable, OnModuleInit } from '@nestjs/common';
import { TRIP_STARTED_ROUTING_KEY, TripStartedEventSchema } from '@vietride/contracts';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { createHash, randomUUID } from 'node:crypto';
import pino from 'pino';
import { z } from 'zod';
import { trackingEtaBatchLockKey, trackingEtaStateKey } from '../eta/eta.constants';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type { RouteGeometryProvider } from '../off-route/route-geometry.provider';
import { RouteStateGenerationRegistry } from '../route-state/route-state-generation.registry';
import { trackingEtaKey } from './location.constants';

const QUEUE_NAME = 'tracking:trip-started-state-invalidation';
const PROCESSING_TTL_SECONDS = 120;
const PROCESSED_TTL_SECONDS = 86_400;
const ETA_SCAN_COUNT = 100;
const EVENT_ID_SCHEMA = z.string().uuid();
const SAFE_BROKER_IDENTIFIER = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const CONSUMER_OPTIONS = {
  prefetch: 1,
  deadLetter: true,
  maxRetries: 5,
  retryDelayMs: 10_000,
} as const;

const MARK_PROCESSED_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])
redis.call('DEL', KEYS[1])
return 1
`;

const RELEASE_LOCK_SCRIPT = `
local current = redis.call('GET', KEYS[1])
if current ~= ARGV[1] then return 0 end
redis.call('DEL', KEYS[1])
return 1
`;

@Injectable()
export class TripStartedStateInvalidationConsumer implements OnModuleInit {
  private readonly logger = pino({ name: TripStartedStateInvalidationConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    @Inject(ROUTE_GEOMETRY_PROVIDER) private readonly routeGeometry: RouteGeometryProvider,
    private readonly routeStateGeneration: RouteStateGenerationRegistry,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      TRIP_STARTED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      CONSUMER_OPTIONS,
    );
  }

  private async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const messageIdentity = this.resolveMessageIdentity(payload, raw);
    const client = this.redis.getClient();
    const processedKey = this.processedKey(messageIdentity);
    const processingKey = this.processingKey(messageIdentity);
    if (await client.get(processedKey)) return;

    const ownerToken = randomUUID();
    const acquired = await client.set(processingKey, ownerToken, 'EX', PROCESSING_TTL_SECONDS, 'NX');
    if (acquired !== 'OK') throw new Error(`TRIP_STARTED_EVENT_LOCKED_${messageIdentity}`);

    try {
      if (await client.get(processedKey)) {
        await this.releaseOwnedLock(processingKey, ownerToken);
        return;
      }

      const parsed = TripStartedEventSchema.safeParse(payload);
      if (!parsed.success) {
        this.logger.warn(
          {
            messageIdentity,
            issues: parsed.error.issues.map((issue) => ({ code: issue.code, path: issue.path })),
          },
          'Dropping malformed trip-started event',
        );
        await this.requireMarkedProcessed(processingKey, processedKey, ownerToken);
        return;
      }

      await this.invalidateStartedTripState(parsed.data.tripId);
      await this.requireMarkedProcessed(processingKey, processedKey, ownerToken);
      this.logger.info(
        { messageIdentity, tripId: parsed.data.tripId },
        'Invalidated pre-departure Tracking state after trip start',
      );
    } catch (error) {
      await this.releaseOwnedLock(processingKey, ownerToken);
      throw error;
    }
  }

  private async invalidateStartedTripState(tripId: string): Promise<void> {
    this.routeStateGeneration.invalidate(tripId);
    this.routeGeometry.invalidateRouteGeometry(tripId);

    const client = this.redis.getClient();
    await client.del(
      trackingEtaStateKey(tripId),
      trackingEtaBatchLockKey(tripId),
    );

    let cursor = '0';
    do {
      const [nextCursor, keys] = await client.scan(
        cursor,
        'MATCH',
        trackingEtaKey(tripId, '*'),
        'COUNT',
        ETA_SCAN_COUNT,
      );
      if (keys.length > 0) await client.del(...keys);
      cursor = nextCursor;
    } while (cursor !== '0');
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
      PROCESSED_TTL_SECONDS,
    );
    if (Number(result) !== 1) throw new Error(`TRIP_STARTED_EVENT_LOCK_NOT_OWNED_${processedKey}`);
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
    return `tracking:trip_started:processed:${messageIdentity}`;
  }

  private processingKey(messageIdentity: string): string {
    return `tracking:trip_started:processing:${messageIdentity}`;
  }
}
