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
  SHUTTLE_GPS_IDEMPOTENCY_TTL_SECONDS,
  SHUTTLE_LATEST_TTL_SECONDS,
  shuttleBufferKey,
  shuttleEtaKey,
  shuttleEtaStateKey,
  shuttleGpsIdempotencyKey,
  shuttleLatestKey,
} from './shuttle.constants';
import type { ShuttleGpsUpdateDto } from './shuttle.dto';

export const ShuttleTrackingStopSchema = z.object({
  pickupOrder: z.number().int().positive(),
  bookingId: z.string().uuid().nullable().optional(),
  latitude: z.number(),
  longitude: z.number(),
  status: z.string(),
  isStation: z.boolean(),
});
export const ShuttleTrackingContextSchema = z.object({
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  driverUserId: z.string().uuid(),
  allowed: z.boolean(),
  scope: z.string().nullable().optional(),
  stops: z.array(ShuttleTrackingStopSchema),
});
const EnvelopeSchema = z.object({
  success: z.boolean(),
  data: ShuttleTrackingContextSchema.optional(),
});

export type ShuttleTrackingContext = z.infer<typeof ShuttleTrackingContextSchema>;
export type ShuttleTrackingStop = z.infer<typeof ShuttleTrackingStopSchema>;

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
      const direct = ShuttleTrackingContextSchema.safeParse(body);
      if (direct.success) return direct.data;
      const envelope = EnvelopeSchema.parse(body);
      if (!envelope.data) throw new Error('TRACKING_AUTH_UNAVAILABLE');
      return envelope.data;
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

}
