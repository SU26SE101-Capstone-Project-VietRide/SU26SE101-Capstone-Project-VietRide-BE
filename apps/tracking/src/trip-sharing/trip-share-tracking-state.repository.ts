import { Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { trackingEtaKey, trackingLatestKey } from '../location/location.constants';
import { UpdateLocationSchema } from '../location/dto/update-location.dto';
import { EtaResponseSchema, type EtaResponseDto } from '../tracking-data/dto/eta-response.dto';

export type TripShareLatestState = import('../location/dto/update-location.dto').UpdateLocationDto;

@Injectable()
export class TripShareTrackingStateRepository {
  constructor(private readonly redis: RedisService) {}

  async findLatest(tripId: string): Promise<TripShareLatestState | null> {
    const payload = await this.redis.getClient().get(trackingLatestKey(tripId));
    const parsed = this.parse(payload, UpdateLocationSchema);
    if (!parsed || parsed.tripId.toLowerCase() !== tripId.toLowerCase()) return null;
    return parsed;
  }

  async findEta(tripId: string, stopId: string): Promise<EtaResponseDto | null> {
    const payload = await this.redis.getClient().get(trackingEtaKey(tripId, stopId));
    const parsed = this.parse(payload, EtaResponseSchema);
    if (!parsed
      || parsed.tripId.toLowerCase() !== tripId.toLowerCase()
      || parsed.stopId.toLowerCase() !== stopId.toLowerCase()) {
      return null;
    }
    return parsed;
  }

  private parse<T>(payload: string | null, schema: { safeParse(value: unknown): { success: true; data: T } | { success: false } }): T | null {
    if (!payload) return null;
    let value: unknown;
    try {
      value = JSON.parse(payload);
    } catch {
      return null;
    }
    const parsed = schema.safeParse(value);
    return parsed.success ? parsed.data : null;
  }
}
