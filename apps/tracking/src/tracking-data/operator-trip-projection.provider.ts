import { Inject, Injectable } from '@nestjs/common';
import { z } from 'zod';
import { randomUUID } from 'node:crypto';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';

const ProjectionSchema = z.array(z.object({ tripId: z.string().uuid(), status: z.string() }));
const ProjectionEnvelopeSchema = z.object({
  success: z.literal(true),
  data: ProjectionSchema,
});
const ProjectionResponseSchema = z.union([ProjectionSchema, ProjectionEnvelopeSchema]);
export type OperatorTripProjection = z.infer<typeof ProjectionSchema>[number];

interface CacheEntry { expiresAt: number; items: OperatorTripProjection[] }

@Injectable()
export class OperatorTripProjectionProvider {
  private readonly cache = new Map<string, CacheEntry>();

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async list(operatorId: string, status?: string): Promise<OperatorTripProjection[]> {
    const key = `${operatorId}:${status ?? '*'}`;
    const cached = this.cache.get(key);
    if (cached && cached.expiresAt > Date.now()) return cached.items;
    const token = await this.signer.sign({ sub: 'tracking-service', reqId: randomUUID() });
    const url = new URL(`/internal/v1/operators/${operatorId}/tracking-trips`, this.env.TRIP_SERVICE_BASE_URL);
    if (status) url.searchParams.set('status', status);
    const response = await fetch(url, {
      headers: { 'X-Internal-Auth': `Bearer ${token}` },
      signal: AbortSignal.timeout(this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS),
    });
    if (!response.ok) throw new Error(`TRIP_FLEET_PROJECTION_FAILED_${response.status}`);
    const parsed = ProjectionResponseSchema.parse(await response.json());
    const items = Array.isArray(parsed) ? parsed : parsed.data;
    this.cache.set(key, { items, expiresAt: Date.now() + 5_000 });
    return items;
  }
}
