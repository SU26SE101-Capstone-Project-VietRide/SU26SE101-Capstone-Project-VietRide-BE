import { Controller, Get, Headers, HttpStatus, Param, Query, Res, UseGuards } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiParam, ApiQuery, ApiResponse, ApiTags } from '@nestjs/swagger';
import type { Response } from 'express';
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
  type EtaBatchTrackingResponseDto,
  type LatestTrackingResponseDto,
  type TrailTrackingResponseDto,
  TrackingDataService,
} from './tracking-data.service';
import { TrackingDataAuthGuard } from './tracking-data-auth.guard';
import {
  ApiErrorEnvelopeDto,
  TrackingEtaEnvelopeDto,
  TrackingEtaBatchEnvelopeDto,
  TrackingLatestEnvelopeDto,
  TrackingTrailEnvelopeDto,
} from './dto/swagger-response.dto';
import { PublicTripRouteContextEnvelopeSwaggerDto } from './dto/route-context-response.dto';
import {
  type PublicTripRouteContextDto,
  TripRouteContextService,
} from './trip-route-context.service';

const PUBLIC_ROUTE_POLYLINE_CACHE_MAX_AGE_SECONDS = 600;
const PUBLIC_ROUTE_FALLBACK_CACHE_MAX_AGE_SECONDS = 30;

@ApiTags('Tracking')
@ApiBearerAuth()
@UseGuards(TrackingDataAuthGuard)
@Controller('/v1/tracking/trips')
export class TrackingDataController {
  constructor(
    private readonly trackingDataService: TrackingDataService,
    private readonly tripRouteContextService: TripRouteContextService,
  ) {}

  @Get(':tripId/route-geometry')
  @ApiOperation({ summary: 'Get authorized public route geometry and map markers for a trip' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiResponse({ status: 200, description: 'Sanitized route geometry and map markers.', type: PublicTripRouteContextEnvelopeSwaggerDto })
  @ApiResponse({ status: 304, description: 'The authorized route context has not changed.' })
  @ApiResponse({ status: 400, description: 'Validation error (invalid tripId).', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Unauthorized - missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'User is not allowed to view this trip.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization or route context provider unavailable.', type: ApiErrorEnvelopeDto })
  async getRouteGeometry(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
    @Headers('if-none-match') ifNoneMatch: string | undefined,
    @Res({ passthrough: true }) response: Response,
  ): Promise<PublicTripRouteContextDto | undefined> {
    const result = await this.tripRouteContextService.getRouteContext(params.tripId);
    const cacheMaxAge = result.data.geometry?.source === 'ROUTE_POLYLINE'
      ? PUBLIC_ROUTE_POLYLINE_CACHE_MAX_AGE_SECONDS
      : PUBLIC_ROUTE_FALLBACK_CACHE_MAX_AGE_SECONDS;
    response.setHeader('Cache-Control', `private, max-age=${cacheMaxAge}`);
    response.setHeader('Vary', 'Authorization');
    response.setHeader('ETag', result.etag);
    if (ifNoneMatch?.split(',').some((candidate) => candidate.trim() === result.etag)) {
      response.status(HttpStatus.NOT_MODIFIED);
      return undefined;
    }

    return result.data;
  }

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
  ): Promise<LatestTrackingResponseDto> {
    return this.trackingDataService.getLatest(params.tripId);
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
  ): Promise<TrailTrackingResponseDto> {
    return this.trackingDataService.getTrail(params.tripId, query);
  }

  @Get(':tripId/eta')
  @ApiOperation({ summary: 'Get ETA for a specific stop or the inferred next stop' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiQuery({ name: 'stopId', type: 'string', format: 'uuid', required: false, description: 'Optional stop ID; omitted selects the next stop' })
  @ApiQuery({ name: 'targetKind', enum: ['STOP', 'STATION'], required: false, description: 'Explicit ETA target kind' })
  @ApiQuery({ name: 'stationId', type: 'string', format: 'uuid', required: false, description: 'Destination station ID when targetKind=STATION' })
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
  ): Promise<EtaTrackingResponseDto> {
    return this.trackingDataService.getEta(params.tripId, query);
  }

  @Get(':tripId/etas')
  @ApiOperation({ summary: 'Get cached ETA values for all remaining stops and destination' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid', description: 'The ID of the trip' })
  @ApiResponse({ status: 200, description: 'Ordered cached ETA values.', type: TrackingEtaBatchEnvelopeDto })
  @ApiResponse({ status: 400, description: 'Validation error.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Unauthorized.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Forbidden.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization provider unavailable.', type: ApiErrorEnvelopeDto })
  async getEtas(
    @Param(new ZodValidationPipe(TripIdParamSchema)) params: TripIdParamDto,
  ): Promise<EtaBatchTrackingResponseDto> {
    return this.trackingDataService.getEtas(params.tripId);
  }
}
