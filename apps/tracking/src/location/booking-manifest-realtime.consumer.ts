import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ConsumeMessage } from 'amqplib';
import { randomUUID } from 'node:crypto';
import {
  BOOKING_CANCELLED_ROUTING_KEY,
  BOOKING_TRANSFERRED_ROUTING_KEY,
  BookingTransferredEventSchema,
  OperationalBookingCancelledEventSchema,
  PASSENGER_BOARDED_ROUTING_KEY,
  PassengerBoardedEventSchema,
} from '@vietride/contracts';
import { LocationGateway } from './location.gateway';

const PROCESSED_TTL_SECONDS = 86_400;
const PROCESSING_TTL_SECONDS = 300;
const TERMINAL_TRIP_REASONS = new Set(['OPERATOR_CANCELLED_TRIP', 'TRIP_DISRUPTED']);
const RELEASE_LOCK_SCRIPT = `
  if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
  end
  return 0
`;

@Injectable()
export class BookingManifestRealtimeConsumer implements OnModuleInit {
  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly redis: RedisService,
    private readonly gateway: LocationGateway,
  ) {}

  async onModuleInit(): Promise<void> {
    await Promise.all([
      this.consumer.subscribe(
        'tracking:booking-cancelled',
        BOOKING_CANCELLED_ROUTING_KEY,
        (payload, raw) => this.handleCancelled(payload, raw),
        retryOptions(),
      ),
      this.consumer.subscribe(
        'tracking:passenger-boarded',
        PASSENGER_BOARDED_ROUTING_KEY,
        (payload, raw) => this.handleBoarded(payload, raw),
        retryOptions(),
      ),
      this.consumer.subscribe(
        'tracking:booking-transferred',
        BOOKING_TRANSFERRED_ROUTING_KEY,
        (payload, raw) => this.handleTransferred(payload, raw),
        retryOptions(),
      ),
    ]);
  }

  async handleCancelled(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.process('cancelled', payload, raw, () => {
      const parsed = OperationalBookingCancelledEventSchema.safeParse(payload);
      if (
        parsed.success &&
        parsed.data.previousStatus === 'CONFIRMED' &&
        !TERMINAL_TRIP_REASONS.has(parsed.data.cancellationReason)
      ) {
        this.gateway.emitBookingCancelled(parsed.data);
      }
    });
  }

  async handleBoarded(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.process('boarded', payload, raw, () => {
      const parsed = PassengerBoardedEventSchema.safeParse(payload);
      if (parsed.success) this.gateway.emitPassengerBoarded(parsed.data);
    });
  }

  async handleTransferred(payload: unknown, raw: ConsumeMessage): Promise<void> {
    await this.process('transferred', payload, raw, () => {
      const parsed = BookingTransferredEventSchema.safeParse(payload);
      if (parsed.success) this.gateway.emitBookingTransferred(parsed.data);
    });
  }

  private async process(
    kind: string,
    payload: unknown,
    raw: ConsumeMessage,
    emit: () => void,
  ): Promise<void> {
    const messageId = getPayloadEventId(payload) ?? getMessageId(raw);
    if (!messageId) throw new Error(`MISSING_MESSAGE_ID_booking_manifest_${kind}`);

    const client = this.redis.getClient();
    const processedKey = `tracking:booking_manifest:${kind}:processed:${messageId}`;
    const processingKey = `tracking:booking_manifest:${kind}:processing:${messageId}`;
    if (await client.get(processedKey)) return;
    const ownerToken = randomUUID();
    if ((await client.set(processingKey, ownerToken, 'EX', PROCESSING_TTL_SECONDS, 'NX')) !== 'OK') {
      throw new Error(`MESSAGE_LOCKED_booking_manifest_${kind}_${messageId}`);
    }

    try {
      emit();
      await client.set(processedKey, '1', 'EX', PROCESSED_TTL_SECONDS);
    } catch (error) {
      await releaseOwnedLock(client, processingKey, ownerToken);
      throw error;
    }
    await releaseOwnedLock(client, processingKey, ownerToken);
  }
}

function retryOptions() {
  return { prefetch: 1, deadLetter: true, maxRetries: 5, retryDelayMs: 10_000 };
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
