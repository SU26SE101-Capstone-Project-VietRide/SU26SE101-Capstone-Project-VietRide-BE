import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { z } from 'zod';
import { createHash, randomUUID } from 'crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';
import {
  SHUTTLE_BUFFER_MAX_ITEMS,
  SHUTTLE_BUFFER_TTL_SECONDS,
  SHUTTLE_GPS_IDEMPOTENCY_TTL_SECONDS,
  SHUTTLE_LATEST_TTL_SECONDS,
  shuttleBufferKey,
  shuttleEtaKey,
  shuttleEtaStateKey,
  shuttleGpsIdempotencyKey,
  shuttleLatestKey,
} from './shuttle.constants';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';

export const ShuttleDirectionSchema = z.enum([
  'INBOUND_TO_STATION',
  'OUTBOUND_FROM_STATION',
]);
export type ShuttleDirection = z.infer<typeof ShuttleDirectionSchema>;

export const shuttleTrackingStopSchema = z.object({
  pickupOrder: z.number().int().positive(),
  bookingId: z.string().uuid().nullable().optional(),
  latitude: z.number(),
  longitude: z.number(),
  status: z.string(),
  isStation: z.boolean(),
  isOwnPickup: z.boolean().optional(),
  serviceAddress: z.string().trim().min(1).optional(),
  serviceOrder: z.number().int().positive().optional(),
  roadDistanceMeters: z.number().nonnegative().optional(),
  roadDistanceSnapshotMeters: z.number().nonnegative().nullable().optional(),
  passengerCount: z.number().int().nonnegative().nullable().optional(),
  pickedUpAt: z.string().datetime({ offset: true }).nullable().optional(),
  deliveredAt: z.string().datetime({ offset: true }).nullable().optional(),
  statusReason: z.string().nullable().optional(),
});
export const shuttleTrackingStationSchema = z.object({
  stationId: z.string().uuid(),
  name: z.string(),
  latitude: z.number().nullable(),
  longitude: z.number().nullable(),
  pickupOrder: z.number().int().positive(),
});
export const shuttleTrackingContextSchema = z.object({
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  driverUserId: z.string().uuid(),
  allowed: z.boolean(),
  scope: z.string().nullable().optional(),
  direction: ShuttleDirectionSchema.optional(),
  status: z.string().optional(),
  stops: z.array(shuttleTrackingStopSchema),
  station: shuttleTrackingStationSchema.nullable().optional(),
});
const envelopeSchema = z.object({
  success: z.literal(true),
  data: shuttleTrackingContextSchema.optional(),
});

export type ShuttleTrackingContext = z.infer<typeof shuttleTrackingContextSchema>;
export type ShuttleTrackingStop = z.infer<typeof shuttleTrackingStopSchema>;

export interface ShuttlePassengerPickupDto {
  bookingId: string;
  pickupOrder: number;
  serviceAddress?: string;
  serviceOrder?: number;
  roadDistanceMeters?: number;
  latitude: number;
  longitude: number;
  status: 'PENDING' | 'PICKED_UP';
  stopsBeforePickup: number;
}

export interface ShuttlePassengerContextDto {
  shuttleTripId: string;
  mainTripId: string;
  direction?: ShuttleDirection;
  ownPickups: ShuttlePassengerPickupDto[];
  station: {
    stationId: string;
    name: string;
    latitude: number;
    longitude: number;
    pickupOrder: number;
  } | null;
}

export interface ShuttleOperatorStopDto {
  pickupOrder: number;
  bookingId: string | null;
  latitude: number;
  longitude: number;
  status: string;
  isStation: boolean;
  serviceAddress?: string;
  serviceOrder?: number;
  roadDistanceMeters?: number;
  passengerCount: number | null;
  pickedUpAt: string | null;
  deliveredAt: string | null;
  statusReason: string | null;
}

export interface ShuttleOperatorContextDto {
  shuttleTripId: string;
  mainTripId: string;
  direction: ShuttleDirection;
  status: string;
  stops: ShuttleOperatorStopDto[];
  station: {
    stationId: string;
    name: string;
    latitude: number;
    longitude: number;
    pickupOrder: number;
  } | null;
}

export type ShuttleGpsEvent = ShuttleGpsUpdateDto;
const RECORD_SHUTTLE_GPS_SCRIPT = `
local existing = redis.call('GET', KEYS[1])
if existing then
  if existing == ARGV[1] then return 0 end
  return -1
end
redis.call('SET', KEYS[1], ARGV[1], 'EX', tonumber(ARGV[3]))
redis.call('SET', KEYS[2], ARGV[2], 'EX', tonumber(ARGV[4]))
redis.call('RPUSH', KEYS[3], ARGV[2])
redis.call('LTRIM', KEYS[3], -tonumber(ARGV[5]), -1)
redis.call('EXPIRE', KEYS[3], tonumber(ARGV[6]))
return 1
`;

const GPS_ACCEPTED = 1;
const GPS_DUPLICATE = 0;
const GPS_PAYLOAD_MISMATCH = -1;
const TERMINAL_PICKUP_STATUSES = new Set(['PICKED_UP', 'DELIVERED', 'NO_SHOW', 'CANCELLED']);
const passengerEtaStateSchema = z.object({
  nextPickupOrder: z.number().int().positive().optional(),
  order: z.number().int().positive().optional(),
});

@Injectable()
export class ShuttleService {
  constructor(
    private readonly redis: RedisService,
    private readonly signer: TrackingInternalJwtSigner,
    @Inject(ENV_TOKEN) private readonly env: Env,
  ) {}

  async getContext(
    user: TrackingUser,
    shuttleTripId: string,
  ): Promise<ShuttleTrackingContext> {
    const claims = {
      sub: user.userId,
      role: user.role,
      reqId: randomUUID(),
      ...(user.operatorId ? { operatorId: user.operatorId } : {}),
    };
    const token = await this.signer.sign(claims);
    const url = new URL(
      `/internal/v1/shuttle-trips/${encodeURIComponent(shuttleTripId)}/tracking-context`,
      this.env.TRIP_SERVICE_BASE_URL,
    );
    url.searchParams.set('userId', user.userId);
    url.searchParams.set('role', user.role);
    if (user.operatorId) url.searchParams.set('operatorId', user.operatorId);
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_AUTH_HTTP_TIMEOUT_MS);
    try {
      const response = await fetch(url, {
        headers: { 'X-Internal-Auth': `Bearer ${token}` },
        signal: controller.signal,
      });
      if (!response.ok)
        throw new Error(
          response.status === 404 ? 'SHUTTLE_TRIP_NOT_FOUND' : 'TRACKING_AUTH_UNAVAILABLE',
        );
      const body: unknown = await response.json();
      const direct = shuttleTrackingContextSchema.safeParse(body);
      if (direct.success) return direct.data;
      const envelope = envelopeSchema.safeParse(body);
      if (!envelope.success || !envelope.data.data) {
        throw new Error('TRACKING_CONTEXT_UNAVAILABLE');
      }
      return envelope.data.data;
    } finally {
      clearTimeout(timeout);
    }
  }

  async recordLocation(dto: ShuttleGpsUpdateDto): Promise<{
    gps: ShuttleGpsEvent;
    duplicate: boolean;
  }> {
    const client = this.redis.getClient();
    const payload = JSON.stringify(dto);
    const bufferKey = shuttleBufferKey(dto.shuttleTripId);
    const fingerprint = createHash('sha256').update(payload).digest('hex');
    const result = Number(
      await client.eval(
        RECORD_SHUTTLE_GPS_SCRIPT,
        3,
        shuttleGpsIdempotencyKey(dto.shuttleTripId, dto.recordedAt),
        shuttleLatestKey(dto.shuttleTripId),
        bufferKey,
        fingerprint,
        payload,
        String(SHUTTLE_GPS_IDEMPOTENCY_TTL_SECONDS),
        String(SHUTTLE_LATEST_TTL_SECONDS),
        String(SHUTTLE_BUFFER_MAX_ITEMS),
        String(SHUTTLE_BUFFER_TTL_SECONDS),
      ),
    );
    if (result === GPS_DUPLICATE) {
      return { gps: dto, duplicate: true };
    }
    if (result === GPS_PAYLOAD_MISMATCH) {
      throw new Error('GPS_OPERATION_PAYLOAD_MISMATCH');
    }
    if (result !== GPS_ACCEPTED) {
      throw new Error(`Unexpected Redis idempotency result: ${result}`);
    }
    return { gps: dto, duplicate: false };
  }

  async getLatest(shuttleTripId: string): Promise<unknown> {
    const raw = await this.redis.getClient().get(shuttleLatestKey(shuttleTripId));
    return raw ? (JSON.parse(raw) as unknown) : null;
  }

  async getEta(shuttleTripId: string): Promise<unknown> {
    const state = await this.redis.getClient().get(shuttleEtaStateKey(shuttleTripId));
    if (!state) return null;
    const parsed = JSON.parse(state) as { order: number };
    const raw = await this.redis.getClient().get(shuttleEtaKey(shuttleTripId, parsed.order));
    return raw ? (JSON.parse(raw) as unknown) : null;
  }

  async getPassengerContext(
    context: ShuttleTrackingContext,
  ): Promise<ShuttlePassengerContextDto> {
    if (context.station === undefined) throw this.contextUnavailable();
    const ownPickups = context.stops
      .filter((stop) => stop.isOwnPickup && (stop.status === 'PENDING' || stop.status === 'PICKED_UP'));
    if (ownPickups.length === 0) throw this.contextUnavailable();
    if (ownPickups.some((stop) =>
      !stop.bookingId
      || !stop.serviceAddress
      || !this.isValidCoordinate(stop.latitude, stop.longitude))) {
      throw this.contextUnavailable();
    }

    const currentOrder = await this.readCurrentPickupOrder(context);
    const mappedPickups = ownPickups
      .map((stop): ShuttlePassengerPickupDto => ({
        bookingId: stop.bookingId as string,
        pickupOrder: stop.pickupOrder,
        serviceAddress: stop.serviceAddress as string,
        serviceOrder: stop.serviceOrder ?? stop.pickupOrder,
        ...(stop.roadDistanceMeters !== undefined
          ? { roadDistanceMeters: stop.roadDistanceMeters }
          : typeof stop.roadDistanceSnapshotMeters === 'number'
            ? { roadDistanceMeters: stop.roadDistanceSnapshotMeters }
            : {}),
        latitude: stop.latitude,
        longitude: stop.longitude,
        status: stop.status as 'PENDING' | 'PICKED_UP',
        stopsBeforePickup: stop.status === 'PICKED_UP'
          ? 0
          : this.countStopsBeforePickup(context.stops, currentOrder, stop.pickupOrder),
      }))
      .sort((left, right) => left.pickupOrder - right.pickupOrder
        || left.bookingId.localeCompare(right.bookingId));

    const station = context.station
      && context.station.latitude !== null
      && context.station.longitude !== null
      && this.isValidCoordinate(context.station.latitude, context.station.longitude)
      ? {
          stationId: context.station.stationId,
          name: context.station.name,
          latitude: context.station.latitude,
          longitude: context.station.longitude,
          pickupOrder: context.station.pickupOrder,
        }
      : null;

    return {
      shuttleTripId: context.shuttleTripId,
      mainTripId: context.mainTripId,
      direction: context.direction ?? 'INBOUND_TO_STATION',
      ownPickups: mappedPickups,
      station,
    };
  }

  getOperatorContext(context: ShuttleTrackingContext): ShuttleOperatorContextDto {
    if (context.scope !== 'OPERATOR'
      || context.direction === undefined
      || context.status === undefined
      || context.station === undefined
      || context.stops.some((stop) => !this.isValidCoordinate(stop.latitude, stop.longitude))) {
      throw this.contextUnavailable();
    }

    const stops = context.stops
      .map((stop): ShuttleOperatorStopDto => ({
        pickupOrder: stop.pickupOrder,
        bookingId: stop.bookingId ?? null,
        latitude: stop.latitude,
        longitude: stop.longitude,
        status: stop.status,
        isStation: stop.isStation,
        passengerCount: stop.isStation ? null : (stop.passengerCount ?? null),
        pickedUpAt: stop.isStation ? null : (stop.pickedUpAt ?? null),
        deliveredAt: stop.isStation ? null : (stop.deliveredAt ?? null),
        statusReason: stop.isStation ? null : (stop.statusReason ?? null),
        ...(stop.serviceAddress !== undefined ? { serviceAddress: stop.serviceAddress } : {}),
        ...(stop.serviceOrder !== undefined ? { serviceOrder: stop.serviceOrder } : {}),
        ...(stop.roadDistanceMeters !== undefined
          ? { roadDistanceMeters: stop.roadDistanceMeters }
          : typeof stop.roadDistanceSnapshotMeters === 'number'
            ? { roadDistanceMeters: stop.roadDistanceSnapshotMeters }
            : {}),
      }))
      .sort((left, right) => left.pickupOrder - right.pickupOrder
        || (left.bookingId ?? '').localeCompare(right.bookingId ?? ''));

    const station = context.station
      && context.station.latitude !== null
      && context.station.longitude !== null
      && this.isValidCoordinate(context.station.latitude, context.station.longitude)
      ? {
          stationId: context.station.stationId,
          name: context.station.name,
          latitude: context.station.latitude,
          longitude: context.station.longitude,
          pickupOrder: context.station.pickupOrder,
        }
      : null;

    return {
      shuttleTripId: context.shuttleTripId,
      mainTripId: context.mainTripId,
      direction: context.direction,
      status: context.status,
      stops,
      station,
    };
  }

  private async readCurrentPickupOrder(context: ShuttleTrackingContext): Promise<number | undefined> {
    const raw = await this.redis.getClient().get(shuttleEtaStateKey(context.shuttleTripId));
    if (raw) {
      try {
        const parsed = passengerEtaStateSchema.safeParse(JSON.parse(raw) as unknown);
        if (parsed.success) return parsed.data.nextPickupOrder ?? parsed.data.order;
      } catch {
        // Fall through to the manifest-derived order when ETA state is unavailable or malformed.
      }
    }

    return context.stops
      .filter((stop) => !stop.isStation && !TERMINAL_PICKUP_STATUSES.has(stop.status))
      .sort((left, right) => left.pickupOrder - right.pickupOrder)[0]?.pickupOrder;
  }

  private countStopsBeforePickup(
    stops: ShuttleTrackingStop[],
    currentOrder: number | undefined,
    ownPickupOrder: number,
  ): number {
    if (currentOrder === undefined || currentOrder >= ownPickupOrder) return 0;
    return new Set(stops
      .filter((stop) => !stop.isStation
        && !TERMINAL_PICKUP_STATUSES.has(stop.status)
        && stop.pickupOrder >= currentOrder
        && stop.pickupOrder < ownPickupOrder)
      .map((stop) => stop.pickupOrder)).size;
  }

  private isValidCoordinate(latitude: number, longitude: number): boolean {
    return Number.isFinite(latitude)
      && Number.isFinite(longitude)
      && latitude >= -90
      && latitude <= 90
      && longitude >= -180
      && longitude <= 180;
  }

  private contextUnavailable(): ServiceUnavailableException {
    return new ServiceUnavailableException({
      errorCode: 'TRACKING_CONTEXT_UNAVAILABLE',
      detail: 'Shuttle tracking context is unavailable',
    });
  }

}
