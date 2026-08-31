import { Injectable, OnModuleInit } from '@nestjs/common';
import { RabbitMqConsumer } from '@vietride/nest-rabbitmq';
import type { ConsumeMessage } from 'amqplib';
import { createHash } from 'node:crypto';
import pino from 'pino';
import { z } from 'zod';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareMessageIdempotencyRepository } from './trip-share-message-idempotency.repository';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripShareSubstitutionStateRepository } from './trip-share-substitution-state.repository';
import {
  TRIP_SHARE_VEHICLE_SUBSTITUTED_CONSUMER_OPTIONS,
  TRIP_SHARE_VEHICLE_SUBSTITUTED_QUEUE,
  TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
  TripShareVehicleSubstitutedEventSchema,
} from './trip-vehicle-substituted-share.constants';

const SAFE_BROKER_IDENTIFIER = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/;
const EVENT_ID_SCHEMA = z.string().uuid();

@Injectable()
export class TripVehicleSubstitutedShareConsumer implements OnModuleInit {
  private readonly logger = pino({ name: TripVehicleSubstitutedShareConsumer.name });

  constructor(
    private readonly consumer: RabbitMqConsumer,
    private readonly idempotency: TripShareMessageIdempotencyRepository,
    private readonly grants: TripShareGrantRepository,
    private readonly substitutions: TripShareSubstitutionStateRepository,
    private readonly realtime: TripShareRealtimePublisher,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.consumer.subscribe(
      TRIP_SHARE_VEHICLE_SUBSTITUTED_QUEUE,
      TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
      (payload, raw) => this.handle(payload, raw),
      TRIP_SHARE_VEHICLE_SUBSTITUTED_CONSUMER_OPTIONS,
    );
  }

  private async handle(payload: unknown, raw: ConsumeMessage): Promise<void> {
    const messageIdentity = this.resolveMessageIdentity(payload, raw);
    if (await this.idempotency.isProcessed(messageIdentity)) return;

    const ownerToken = await this.idempotency.acquire(messageIdentity);
    if (!ownerToken) throw new Error('TRIP_SHARE_VEHICLE_SUBSTITUTED_EVENT_LOCKED');

    try {
      if (await this.idempotency.isProcessed(messageIdentity)) {
        await this.idempotency.release(messageIdentity, ownerToken);
        return;
      }

      const parsed = TripShareVehicleSubstitutedEventSchema.safeParse(payload);
      if (!parsed.success) {
        this.logger.warn(
          {
            messageIdentity,
            issues: parsed.error.issues.map((issue) => ({ code: issue.code, path: issue.path })),
          },
          'Dropping malformed trip-share vehicle-substituted event',
        );
        await this.requireMarkedProcessed(messageIdentity, ownerToken);
        return;
      }

      const event = parsed.data;
      await this.grants.transferActiveGrants(event.oldTripId, event.newTripId, new Date());
      await this.substitutions.storeAlias(event.oldTripId, event.newTripId);
      await this.realtime.transferTrip(event.oldTripId, event.newTripId, event.occurredAt);
      await this.substitutions.clearPending(event.oldTripId);
      await this.requireMarkedProcessed(messageIdentity, ownerToken);
      this.logger.info(
        { messageIdentity, oldTripId: event.oldTripId, newTripId: event.newTripId },
        'Transferred trip-share grants to replacement Trip',
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
    throw new Error(`TRIP_SHARE_VEHICLE_SUBSTITUTED_EVENT_LOCK_NOT_OWNED_${messageIdentity}`);
  }
}
