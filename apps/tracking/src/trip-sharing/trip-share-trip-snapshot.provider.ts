import {
  Inject,
  Injectable,
  NotFoundException,
  ServiceUnavailableException,
} from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import pino from 'pino';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';

const INTERNAL_AUTH_HEADER = 'X-Internal-Auth';
const TRIP_SNAPSHOT_SCHEMA = z.object({
  tripId: z.string().uuid(),
  status: z.string().min(1),
}).passthrough();
const TRIP_SNAPSHOT_ENVELOPE_SCHEMA = z.object({
  success: z.boolean(),
  data: TRIP_SNAPSHOT_SCHEMA.optional().nullable(),
}).passthrough();

export type TripShareTripSnapshot = z.infer<typeof TRIP_SNAPSHOT_SCHEMA>;

@Injectable()
export class TripShareTripSnapshotProvider {
  private readonly logger = pino({ name: TripShareTripSnapshotProvider.name });

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async getTrip(tripId: string): Promise<TripShareTripSnapshot> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS);
    try {
      const token = await this.signer.sign({ sub: 'tracking', reqId: randomUUID() });
      const url = new URL(`/internal/v1/trips/${encodeURIComponent(tripId)}`, this.env.TRIP_SERVICE_BASE_URL);
      const response = await fetch(url, {
        method: 'GET',
        headers: { [INTERNAL_AUTH_HEADER]: `Bearer ${token}` },
        signal: controller.signal,
      });
      if (response.status === 404) this.notFound(tripId);
      if (response.status === 401 || !response.ok) this.unavailable();

      const body = await response.json().catch(() => null);
      const direct = TRIP_SNAPSHOT_SCHEMA.safeParse(body);
      const envelope = TRIP_SNAPSHOT_ENVELOPE_SCHEMA.safeParse(body);
      const snapshot = direct.success
        ? direct.data
        : envelope.success && envelope.data.success && envelope.data.data
          ? envelope.data.data
          : null;
      if (!snapshot || snapshot.tripId.toLowerCase() !== tripId.toLowerCase()) this.unavailable();
      return snapshot;
    } catch (error) {
      if (error instanceof NotFoundException || error instanceof ServiceUnavailableException) throw error;
      this.logger.warn({ tripId }, 'Trip snapshot provider unavailable');
      this.unavailable();
    } finally {
      clearTimeout(timeout);
    }
  }

  private notFound(tripId: string): never {
    throw new NotFoundException({ errorCode: 'TRIP_NOT_FOUND', detail: `Trip ${tripId} not found` });
  }

  private unavailable(): never {
    throw new ServiceUnavailableException({
      errorCode: 'TRACKING_TRIP_UNAVAILABLE',
      detail: 'Trip provider is unavailable',
    });
  }
}
