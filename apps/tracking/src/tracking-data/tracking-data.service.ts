import { Inject, Injectable, Optional } from '@nestjs/common';
import type { EtaQueryDto, TrailQueryDto } from './dto/tracking-data-query.dto';
import type { EtaResponseDto } from './dto/eta-response.dto';
import { TrackingDataRepository } from './tracking-data.repository';
import { TRIP_DATA_PROVIDER } from '../eta/eta.constants';
import type { TripDataProvider } from '../eta/trip-data.provider';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import type {
  DetailedRouteGeometryProvider,
  RouteGeometrySnapshot,
} from '../off-route/route-geometry.provider';
import type { TripStopSnapshot } from '../eta/trip-data.provider';

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

export interface EtaBatchTrackingResponseDto {
  etas: EtaResponseDto[];
}

@Injectable()
export class TrackingDataService {
  constructor(
    private readonly repository: TrackingDataRepository,
    @Optional() @Inject(TRIP_DATA_PROVIDER) private readonly tripData?: TripDataProvider,
    @Optional() @Inject(ROUTE_GEOMETRY_PROVIDER)
    private readonly routeGeometry?: DetailedRouteGeometryProvider,
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
    if (query.targetKind === 'STATION' && query.stationId) {
      const route = await this.getRouteSnapshot(tripId);
      const station = [route?.originStation, route?.destinationStation]
        .find((candidate) => candidate?.stationId === query.stationId);
      if (!station) {
        return { eta: null };
      }
      const eta = await this.repository.findEta(tripId, query.stationId, 'STATION');
      return { eta: eta ? { ...eta, stopName: station.name } : null };
    }

    if (query.stopId) {
      const eta = await this.repository.findEta(tripId, query.stopId, 'STOP');
      const stopName = routeStops.find((stop) => stop.stopId === query.stopId)?.stopName ?? null;
      return { eta: eta ? { ...eta, stopName } : null };
    }

    const route = await this.getRouteSnapshot(tripId);
    for (const target of buildEtaLookupTargets(routeStops, route)) {
      const eta = await this.repository.findEta(tripId, target.id, target.kind);
      if (eta) return { eta: { ...eta, stopName: target.name } };
    }
    return { eta: null };
  }

  async getEtas(tripId: string): Promise<EtaBatchTrackingResponseDto> {
    const routeStops = this.tripData ? await this.tripData.getRouteStops(tripId) : [];
    const route = await this.getRouteSnapshot(tripId);
    const etas: EtaResponseDto[] = [];
    for (const target of buildEtaLookupTargets(routeStops, route)) {
      const eta = await this.repository.findEta(tripId, target.id, target.kind);
      if (eta) {
        etas.push({
          ...eta,
          ...targetMetadata(target),
        });
      }
    }

    return { etas };
  }

  private async getRouteSnapshot(tripId: string): Promise<RouteGeometrySnapshot | null> {
    const route = this.routeGeometry
      ? await this.routeGeometry.getDetailedRouteGeometry(tripId)
      : null;
    return route?.kind === 'ok' ? route.snapshot : null;
  }
}

const COMPLETED_STOP_STATUSES = new Set([
  'COMPLETED',
  'ARRIVED',
  'SKIPPED',
  'PICKED_UP',
  'DROPPED_OFF',
  'CANCELLED',
]);

interface EtaLookupTarget {
  id: string;
  kind: 'STOP' | 'STATION';
  name: string | null;
  sequence?: number;
}

function buildEtaLookupTargets(
  routeStops: TripStopSnapshot[],
  route: RouteGeometrySnapshot | null,
): EtaLookupTarget[] {
  const tripStatus = route?.tripStatus?.toUpperCase() ?? 'IN_PROGRESS';
  if (tripStatus === 'SCHEDULED' || tripStatus === 'BOARDING') {
    return route?.originStation
      ? [{
          id: route.originStation.stationId,
          kind: 'STATION',
          name: route.originStation.name,
        }]
      : [];
  }
  if (tripStatus !== 'IN_PROGRESS') return [];

  const stops: EtaLookupTarget[] = [...routeStops]
    .sort((left, right) => left.sequence - right.sequence)
    .filter((stop) => !stop.status || !COMPLETED_STOP_STATUSES.has(stop.status))
    .map((stop) => ({
      id: stop.stopId,
      kind: 'STOP',
      name: stop.stopName ?? null,
      sequence: stop.sequence,
    }));
  return [
    ...stops,
    ...(route?.destinationStation
      ? [{
          id: route.destinationStation.stationId,
          kind: 'STATION' as const,
          name: route.destinationStation.name,
        }]
      : []),
  ];
}

function targetMetadata(target: EtaLookupTarget): { stopName: string | null; sequence?: number } {
  return {
    stopName: target.name,
    ...(target.sequence !== undefined ? { sequence: target.sequence } : {}),
  };
}
