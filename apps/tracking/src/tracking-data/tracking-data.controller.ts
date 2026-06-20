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
import {
  ApiErrorEnvelopeDto,
  TrackingEtaEnvelopeDto,
  TrackingLatestEnvelopeDto,
  TrackingTrailEnvelopeDto,
} from './dto/swagger-response.dto';

@ApiTags('Tracking')
@ApiBearerAuth()
@Controller('/v1/tracking/trips')
export class TrackingDataController {
  constructor(private readonly trackingDataService: TrackingDataService) {}

  @Get(':tripId/latest')
  @ApiOperation({ summary: 'Get latest location for a trip' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiResponse({ status: 200, description: 'Latest location data.', type: TrackingLatestEnvelopeDto })
  @ApiResponse({ status: 400, description: 'Validation error (invalid tripId).', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Unauthorized — missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Forbidden — user not allowed to view this trip.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 500, description: 'Internal server error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization provider unavailable.', type: ApiErrorEnvelopeDto })
  async getLatest(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<LatestTrackingResponseDto> {
    return this.trackingDataService.getLatest(params.tripId, authorizationHeader);
  }

  @Get(':tripId/trail')
  @ApiOperation({ summary: 'Get location trail for a trip' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiQuery({ name: 'from', type: 'string', format: 'date-time', required: false, description: 'Start time (ISO 8601)' })
  @ApiQuery({ name: 'to', type: 'string', format: 'date-time', required: false, description: 'End time (ISO 8601)' })
  @ApiQuery({ name: 'page', type: 'number', required: false, description: 'Page number (default 1, min 1)' })
  @ApiQuery({ name: 'pageSize', type: 'number', required: false, description: 'Items per page (default 20, max 100)' })
  @ApiQuery({ name: 'sortBy', enum: ['recordedAt'], required: false, description: 'Sort field (default recordedAt)' })
  @ApiQuery({ name: 'sortDir', enum: ['asc', 'desc'], required: false, description: 'Sort direction (default asc)' })
  @ApiResponse({ status: 200, description: 'Location trail items.', type: TrackingTrailEnvelopeDto })
  @ApiResponse({ status: 400, description: 'Validation error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Unauthorized.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Forbidden.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 500, description: 'Internal server error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization provider unavailable.', type: ApiErrorEnvelopeDto })
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
  @ApiResponse({ status: 200, description: 'ETA data.', type: TrackingEtaEnvelopeDto })
  @ApiResponse({ status: 400, description: 'Validation error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Unauthorized.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Forbidden.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 500, description: 'Internal server error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization provider unavailable.', type: ApiErrorEnvelopeDto })
  async getEta(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Query(new ZodValidationPipe(EtaQuerySchema)) query: EtaQueryDto,
    @Headers('authorization') authorizationHeader: string | undefined,
  ): Promise<EtaTrackingResponseDto> {
    return this.trackingDataService.getEta(params.tripId, query, authorizationHeader);
  }
}
