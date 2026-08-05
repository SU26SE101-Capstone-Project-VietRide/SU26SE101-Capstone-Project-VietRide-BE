import { Inject, Injectable, Optional } from '@nestjs/common';
import type { EtaQueryDto, TrailQueryDto } from './dto/tracking-data-query.dto';
import type { EtaResponseDto } from './dto/eta-response.dto';
import { TrackingDataRepository } from './tracking-data.repository';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { TripDataProvider } from '../eta/trip-data.provider';

export interface LatestTrackingResponseDto {
  latest: Awaited<ReturnType<TrackingDataRepository['findLatest']>>;
}

export interface TrailTrackingResponseDto {
  items: Awaited<ReturnType<TrackingDataRepository['findTrail']>>['items'];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface EtaTrackingResponseDto {
  eta: EtaResponseDto | null;
}

@Injectable()
export class TrackingDataService {
  constructor(
    private readonly repository: TrackingDataRepository,
    @Optional() @Inject(TRIP_DATA_PROVIDER) private readonly tripData?: TripDataProvider,
  ) {}

  async getLatest(tripId: string): Promise<LatestTrackingResponseDto> {
    return { latest: await this.repository.findLatest(tripId) };
  }

  async getTrail(
    tripId: string,
    query: TrailQueryDto,
  ): Promise<TrailTrackingResponseDto> {
    const { items, totalItems } = await this.repository.findTrail(tripId, query);
    const totalPages = Math.ceil(totalItems / query.pageSize);
    return {
      items,
      page: query.page,
      pageSize: query.pageSize,
      totalItems,
      totalPages,
      hasNextPage: query.page < totalPages,
      hasPreviousPage: query.page > 1,
    };
  }

  async getEta(
    tripId: string,
    query: EtaQueryDto,
  ): Promise<EtaTrackingResponseDto> {
    const routeStops = this.tripData ? await this.tripData.getRouteStops(tripId) : [];
    if (query.stopId) {
      const eta = await this.repository.findEta(tripId, query.stopId);
      const stopName = routeStops.find((stop) => stop.stopId === query.stopId)?.stopName ?? null;
      return { eta: eta ? { ...eta, stopName } : null };
    }

    if (!await this.repository.findLatest(tripId)) return { eta: null };
    const completed = new Set(['COMPLETED', 'ARRIVED', 'SKIPPED', 'PICKED_UP', 'DROPPED_OFF', 'CANCELLED']);
    for (const stop of [...routeStops].sort((left, right) => left.sequence - right.sequence)) {
      if (stop.status && completed.has(stop.status)) continue;
      const eta = await this.repository.findEta(tripId, stop.stopId);
      if (eta) return { eta: { ...eta, stopName: stop.stopName ?? null } };
    }
    return { eta: null };
  }
}
