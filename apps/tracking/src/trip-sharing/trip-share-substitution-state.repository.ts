import { Inject, Injectable } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';

const MAX_SUBSTITUTION_HOPS = 8;

@Injectable()
export class TripShareSubstitutionStateRepository {
  private readonly ttlSeconds: number;

  constructor(
    private readonly redis: RedisService,
    @Inject(ENV_TOKEN) env: Env,
  ) {
    this.ttlSeconds = env.TRACKING_SHARE_TOKEN_TTL_SECONDS;
  }

  async markPending(tripId: string, occurredAt: string): Promise<void> {
    await this.redis.getClient().set(
      this.pendingKey(tripId),
      occurredAt,
      'EX',
      this.ttlSeconds,
    );
  }

  async clearPending(tripId: string): Promise<void> {
    await this.redis.getClient().del(this.pendingKey(tripId));
  }

  async isPending(tripId: string): Promise<boolean> {
    return (await this.redis.getClient().get(this.pendingKey(tripId))) !== null;
  }

  async storeAlias(oldTripId: string, newTripId: string): Promise<void> {
    const client = this.redis.getClient();
    await Promise.all([
      client.set(this.nextKey(oldTripId), newTripId, 'EX', this.ttlSeconds),
      client.set(this.previousKey(newTripId), oldTripId, 'EX', this.ttlSeconds),
    ]);
  }

  async findPrevious(tripId: string): Promise<string | null> {
    return this.redis.getClient().get(this.previousKey(tripId));
  }

  async listPreviousTripIds(tripId: string): Promise<string[]> {
    const visited = new Set<string>([tripId]);
    const result: string[] = [];
    let current = tripId;

    for (let hop = 0; hop < MAX_SUBSTITUTION_HOPS; hop += 1) {
      const previous = await this.findPrevious(current);
      if (!previous) return result;
      if (visited.has(previous)) throw new Error('TRIP_SHARE_SUBSTITUTION_ALIAS_CYCLE');
      visited.add(previous);
      result.push(previous);
      current = previous;
    }

    throw new Error('TRIP_SHARE_SUBSTITUTION_ALIAS_DEPTH_EXCEEDED');
  }

  async resolveCurrentTripId(tripId: string): Promise<string> {
    const visited = new Set<string>();
    let current = tripId;

    for (let hop = 0; hop < MAX_SUBSTITUTION_HOPS; hop += 1) {
      if (visited.has(current)) throw new Error('TRIP_SHARE_SUBSTITUTION_ALIAS_CYCLE');
      visited.add(current);
      const next = await this.redis.getClient().get(this.nextKey(current));
      if (!next) return current;
      current = next;
    }

    throw new Error('TRIP_SHARE_SUBSTITUTION_ALIAS_DEPTH_EXCEEDED');
  }

  private pendingKey(tripId: string): string {
    return `tracking:trip-share:substitution:pending:${tripId}`;
  }

  private nextKey(tripId: string): string {
    return `tracking:trip-share:substitution:next:${tripId}`;
  }

  private previousKey(tripId: string): string {
    return `tracking:trip-share:substitution:previous:${tripId}`;
  }
}
