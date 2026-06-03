import { Controller, Get, Headers, Param, Query } from '@nestjs/common';
import { ZodValidationPipe } from '@vietride/nest-common';
import {
  EtaQuerySchema,
  TrailQuerySchema,
  TripIdParamSchema,
  type EtaQueryDto,
  type TrailQueryDto,
  type TripIdParamDto,
} from './dto/tracking-data-query.dto';
import {
  type EtaTrackingResponseDto,
  type LatestTrackingResponseDto,
  type TrailTrackingResponseDto,
  TrackingDataService,
} from './tracking-data.service';

@Controller('/api/v1/tracking/trips')
export class TrackingDataController {
  constructor(private readonly trackingDataService: TrackingDataService) {}

  @Get(':tripId/latest')
  async getLatest(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<LatestTrackingResponseDto> {
    return this.trackingDataService.getLatest(params.tripId, authorizationHeader);
  }

  @Get(':tripId/trail')
  async getTrail(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Query(new ZodValidationPipe(TrailQuerySchema)) query: TrailQueryDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<TrailTrackingResponseDto> {
    return this.trackingDataService.getTrail(params.tripId, query, authorizationHeader);
  }

  @Get(':tripId/eta')
  async getEta(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Query(new ZodValidationPipe(EtaQuerySchema)) query: EtaQueryDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<EtaTrackingResponseDto> {
    return this.trackingDataService.getEta(params.tripId, query, authorizationHeader);
  }
}
