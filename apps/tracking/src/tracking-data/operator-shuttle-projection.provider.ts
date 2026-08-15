import { Inject, Injectable } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import { ENV_TOKEN } from '../app/tokens';
import { TrackingInternalJwtSigner } from '../authorization/tracking-internal-jwt.signer';
import type { Env } from '../config/env.schema';

const ProjectionSchema = z.array(z.object({
  shuttleTripId: z.string().uuid(),
  mainTripId: z.string().uuid(),
  status: z.literal('IN_PROGRESS'),
}));
const ProjectionEnvelopeSchema = z.object({
  success: z.literal(true),
  data: ProjectionSchema,
});
const ProjectionResponseSchema = z.union([ProjectionSchema, ProjectionEnvelopeSchema]);

export type OperatorShuttleProjection = z.infer<typeof ProjectionSchema>[number];

interface CacheEntry {
  expiresAt: number;
  items: OperatorShuttleProjection[];
}

@Injectable()
export class OperatorShuttleProjectionProvider {
  private readonly cache = new Map<string, CacheEntry>();

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly signer: TrackingInternalJwtSigner,
  ) {}

  async list(operatorId: string): Promise<OperatorShuttleProjection[]> {
    const cached = this.cache.get(operatorId);
    if (cached && cached.expiresAt > Date.now()) return cached.items;

    const token = await this.signer.sign({ sub: 'tracking-service', reqId: randomUUID() });
    const url = new URL(
      `/internal/v1/operators/${encodeURIComponent(operatorId)}/tracking-shuttle-trips`,
      this.env.TRIP_SERVICE_BASE_URL,
    );
    const response = await fetch(url, {
      headers: { 'X-Internal-Auth': `Bearer ${token}` },
      signal: AbortSignal.timeout(this.env.TRACKING_DATA_PROVIDER_TIMEOUT_MS),
    });
    if (!response.ok) throw new Error(`SHUTTLE_FLEET_PROJECTION_FAILED_${response.status}`);

    const parsed = ProjectionResponseSchema.parse(await response.json());
    const items = Array.isArray(parsed) ? parsed : parsed.data;
    this.cache.set(operatorId, { items, expiresAt: Date.now() + 5_000 });
    return items;
  }
}
