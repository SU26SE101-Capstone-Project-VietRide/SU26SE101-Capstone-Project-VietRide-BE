import { Inject, Injectable } from '@nestjs/common';
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
  SHUTTLE_ETA_TTL_SECONDS,
  SHUTTLE_GPS_IDEMPOTENCY_TTL_SECONDS,
  SHUTTLE_LATEST_TTL_SECONDS,
  shuttleBufferKey,
  shuttleEtaKey,
  shuttleEtaStateKey,
  shuttleGpsIdempotencyKey,
  shuttleLatestKey,
} from './shuttle.constants';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';

const TrackingStopSchema = z.object({
  pickupOrder: z.number().int().positive(),
  bookingId: z.string().uuid().nullable().optional(),
  latitude: z.number(),
  longitude: z.number(),
  status: z.string(),
  isStation: z.boolean(),
});
const ContextSchema = z.object({
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  driverUserId: z.string().uuid(),
  allowed: z.boolean(),
  scope: z.string().nullable().optional(),
  stops: z.array(TrackingStopSchema),
});
const EnvelopeSchema = z.object({ success: z.boolean(), data: ContextSchema.optional() });

export type ShuttleGpsEvent = ShuttleGpsUpdateDto;
export interface ShuttleEtaEvent {
  shuttleTripId: string;
  nextPickupOrder: number;
  etaMinutes: number;
  estimatedArrivalTime: string;
  distanceMeters: number;
  updatedAt: string;
}

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
  ): Promise<z.infer<typeof ContextSchema>> {
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
      const direct = ContextSchema.safeParse(body);
      if (direct.success) return direct.data;
      const envelope = EnvelopeSchema.parse(body);
      if (!envelope.data) throw new Error('TRACKING_AUTH_UNAVAILABLE');
      return envelope.data;
    } finally {
      clearTimeout(timeout);
    }
  }

  async recordLocation(
    dto: ShuttleGpsUpdateDto,
    context: z.infer<typeof ContextSchema>,
  ): Promise<{
    gps: ShuttleGpsEvent;
    eta?: ShuttleEtaEvent;
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
    const eta = await this.calculateEta(dto, context);
    return eta ? { gps: dto, eta, duplicate: false } : { gps: dto, duplicate: false };
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

  private async calculateEta(
    dto: ShuttleGpsUpdateDto,
    context: z.infer<typeof ContextSchema>,
  ): Promise<ShuttleEtaEvent | undefined> {
    const next = context.stops.find((stop) => stop.status !== 'CANCELLED');
    if (!next) return undefined;
    const client = this.redis.getClient();
    const priorRaw = await client.get(shuttleEtaStateKey(dto.shuttleTripId));
    if (priorRaw) {
      const prior = JSON.parse(priorRaw) as {
        latitude: number;
        longitude: number;
        etaMinutes: number;
      };
      const moved = this.distanceMeters(
        dto.latitude,
        dto.longitude,
        prior.latitude,
        prior.longitude,
      );
      if (moved < 500 && prior.etaMinutes >= 15) return undefined;
    }
    const distanceMeters = this.distanceMeters(
      dto.latitude,
      dto.longitude,
      next.latitude,
      next.longitude,
    );
    const speedKmh = dto.speedKmh && dto.speedKmh > 3 ? dto.speedKmh : 30;
    const etaMinutes = Math.max(1, Math.ceil((distanceMeters / 1_000 / speedKmh) * 60));
    const updatedAt = new Date().toISOString();
    const event: ShuttleEtaEvent = {
      shuttleTripId: dto.shuttleTripId,
      nextPickupOrder: next.pickupOrder,
      etaMinutes,
      estimatedArrivalTime: new Date(Date.now() + etaMinutes * 60_000).toISOString(),
      distanceMeters,
      updatedAt,
    };
    await client
      .multi()
      .set(
        shuttleEtaKey(dto.shuttleTripId, next.pickupOrder),
        JSON.stringify(event),
        'EX',
        SHUTTLE_ETA_TTL_SECONDS,
      )
      .set(
        shuttleEtaStateKey(dto.shuttleTripId),
        JSON.stringify({
          order: next.pickupOrder,
          latitude: dto.latitude,
          longitude: dto.longitude,
          etaMinutes,
          updatedAt,
        }),
        'EX',
        SHUTTLE_LATEST_TTL_SECONDS,
      )
      .exec();
    return event;
  }

  private distanceMeters(lat1: number, lng1: number, lat2: number, lng2: number): number {
    const radius = 6_371_000;
    const toRadians = (value: number): number => (value * Math.PI) / 180;
    const dLat = toRadians(lat2 - lat1);
    const dLng = toRadians(lng2 - lng1);
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRadians(lat1)) * Math.cos(toRadians(lat2)) * Math.sin(dLng / 2) ** 2;
    return Math.round(radius * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a)));
  }
}
