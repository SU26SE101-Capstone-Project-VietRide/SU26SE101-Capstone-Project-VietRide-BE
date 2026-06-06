import { Controller, Get, Headers, Param, Query } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiParam, ApiQuery, ApiResponse, ApiTags } from '@nestjs/swagger';
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

@ApiTags('Tracking')
@ApiBearerAuth()
@Controller('/api/v1/tracking/trips')
export class TrackingDataController {
  constructor(private readonly trackingDataService: TrackingDataService) {}

  @Get(':tripId/latest')
  @ApiOperation({ summary: 'Get latest location for a trip' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiResponse({ status: 200, description: 'Latest location data retrieved successfully.' })
  @ApiResponse({ status: 401, description: 'Unauthorized' })
  @ApiResponse({ status: 403, description: 'Forbidden' })
  @ApiResponse({ status: 404, description: 'Trip not found' })
  async getLatest(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<LatestTrackingResponseDto> {
    return this.trackingDataService.getLatest(params.tripId, authorizationHeader);
  }

  @Get(':tripId/trail')
  @ApiOperation({ summary: 'Get location trail for a trip' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiQuery({ name: 'from', type: 'string', format: 'date-time', required: false, description: 'Start time' })
  @ApiQuery({ name: 'to', type: 'string', format: 'date-time', required: false, description: 'End time' })
  @ApiQuery({ name: 'limit', type: 'number', required: false, description: 'Maximum items to return (default 500, max 1000)' })
  @ApiResponse({ status: 200, description: 'Location trail retrieved successfully.' })
  @ApiResponse({ status: 401, description: 'Unauthorized' })
  @ApiResponse({ status: 403, description: 'Forbidden' })
  @ApiResponse({ status: 404, description: 'Trip not found' })
  async getTrail(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Query(new ZodValidationPipe(TrailQuerySchema)) query: TrailQueryDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<TrailTrackingResponseDto> {
    return this.trackingDataService.getTrail(params.tripId, query, authorizationHeader);
  }

  @Get(':tripId/eta')
  @ApiOperation({ summary: 'Get ETA for a specific stop' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiQuery({ name: 'stopId', type: 'string', format: 'uuid', description: 'The ID of the stop' })
  @ApiResponse({ status: 200, description: 'ETA retrieved successfully.' })
  @ApiResponse({ status: 401, description: 'Unauthorized' })
  @ApiResponse({ status: 403, description: 'Forbidden' })
  @ApiResponse({ status: 404, description: 'Trip not found' })
  async getEta(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Query(new ZodValidationPipe(EtaQuerySchema)) query: EtaQueryDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<EtaTrackingResponseDto> {
    return this.trackingDataService.getEta(params.tripId, query, authorizationHeader);
  }
}
