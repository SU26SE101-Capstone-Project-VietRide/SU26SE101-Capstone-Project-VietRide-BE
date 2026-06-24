import { Injectable } from '@nestjs/common';
import type { EtaQueryDto, TrailQueryDto } from './dto/tracking-data-query.dto';
import type { EtaResponseDto } from './dto/eta-response.dto';
import { TrackingDataRepository } from './tracking-data.repository';

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
  constructor(private readonly repository: TrackingDataRepository) {}

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
    return { eta: await this.repository.findEta(tripId, query.stopId) };
  }
}
