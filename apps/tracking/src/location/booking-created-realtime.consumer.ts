import { Injectable, OnModuleInit } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import {
  OperationalBookingCreatedEventSchema,
  BOOKING_CREATED_ROUTING_KEY,
} from '@vietride/contracts';
import { randomUUID } from 'node:crypto';
import { LocationGateway } from './location.gateway';

const QUEUE_NAME = 'tracking:booking-created';
const PROCESSED_TTL_SECONDS = 86_400;
const PROCESSING_TTL_SECONDS = 300;
const RELEASE_LOCK_SCRIPT = `
  if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
  end
  return 0
`;

@Injectable()
export class BookingCreatedRealtimeConsumer implements OnModuleInit {
  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly gateway: LocationGateway,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      QUEUE_NAME,
      BOOKING_CREATED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 },
    );
  }

  async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const parsed = OperationalBookingCreatedEventSchema.safeParse(payload);
    const messageId = parsed.success
      ? parsed.data.eventId
      : getMessageId(raw) ?? getPayloadEventId(payload);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_${BOOKING_CREATED_ROUTING_KEY}`);

    const client = this.redis.getClient();
    const processedKey = `tracking:booking_created:processed:${messageId}`;
    const processingKey = `tracking:booking_created:processing:${messageId}`;
    if (await client.get(processedKey)) return;
    const ownerToken = randomUUID();
    if ((await client.set(processingKey, ownerToken, 'EX', PROCESSING_TTL_SECONDS, 'NX')) !== 'OK') {
      throw new Error(`MESSAGE_LOCKED_${BOOKING_CREATED_ROUTING_KEY}_${messageId}`);
    }

    try {
      if (!parsed.success) {
        await client.set(processedKey, '1', 'EX', PROCESSED_TTL_SECONDS);
      } else {
        this.gateway.emitBookingCreated(parsed.data);
        await client.set(processedKey, '1', 'EX', PROCESSED_TTL_SECONDS);
      }
    } catch (error) {
      await releaseOwnedLock(client, processingKey, ownerToken);
      throw error;
    }

    await releaseOwnedLock(client, processingKey, ownerToken);
  }
}

async function releaseOwnedLock(
  client: ReturnType<RedisService['getClient']>,
  processingKey: string,
  ownerToken: string,
): Promise<void> {
  await client.eval(RELEASE_LOCK_SCRIPT, 1, processingKey, ownerToken);
}

function getMessageId(raw: ConsumeMessage): string | undefined {
  const { messageId, correlationId } = raw.properties;
  if (typeof messageId === 'string' && messageId.length > 0) return messageId;
  return typeof correlationId === 'string' && correlationId.length > 0 ? correlationId : undefined;
}

function getPayloadEventId(payload: unknown): string | undefined {
  if (typeof payload !== 'object' || payload === null || !('eventId' in payload)) return undefined;
  const eventId = (payload as { eventId?: unknown }).eventId;
  return typeof eventId === 'string' && eventId.length > 0 ? eventId : undefined;
}
