import {
  Controller,
  ForbiddenException,
  Get,
  Param,
  Req,
  Res,
  ServiceUnavailableException,
  UseGuards,
} from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiParam, ApiResponse, ApiTags } from '@nestjs/swagger';
import type { Response } from 'express';
import { ZodValidationPipe } from '@vietride/nest-common';
import { ApiErrorEnvelopeDto } from '../tracking-data/dto/swagger-response.dto';
import {
  type AuthorizedShuttleTrackingRequest,
  ShuttleTrackingAuthGuard,
} from './shuttle-tracking-auth.guard';
import { ShuttleTripIdParamSchema, type ShuttleTripIdParamDto } from './shuttle.dto';
import { ShuttlePassengerContextEnvelopeSwaggerDto } from './shuttle-passenger-context-response.dto';
import { ShuttleOperatorContextEnvelopeSwaggerDto } from './shuttle-operator-context-response.dto';
import {
  ShuttleService,
  type ShuttleOperatorContextDto,
  type ShuttlePassengerContextDto,
} from './shuttle.service';

@ApiTags('Shuttle Tracking')
@ApiBearerAuth()
@UseGuards(ShuttleTrackingAuthGuard)
@Controller('v1/tracking/shuttle-trips')
export class ShuttleTrackingController {
  constructor(private readonly service: ShuttleService) {}

  @Get(':shuttleTripId/passenger-context')
  @ApiOperation({ summary: 'Get the authenticated passenger own Shuttle pickup context' })
  @ApiParam({ name: 'shuttleTripId', type: 'string', format: 'uuid' })
  @ApiResponse({ status: 200, description: 'Passenger-safe own pickup context.', type: ShuttlePassengerContextEnvelopeSwaggerDto })
  @ApiResponse({ status: 400, description: 'Invalid shuttleTripId.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Passenger Shuttle tracking access denied.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Shuttle trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization or Shuttle context unavailable.', type: ApiErrorEnvelopeDto })
  async passengerContext(
    @Param(new ZodValidationPipe(ShuttleTripIdParamSchema)) params: ShuttleTripIdParamDto,
    @Req() request: AuthorizedShuttleTrackingRequest,
    @Res({ passthrough: true }) response: Response,
  ): Promise<ShuttlePassengerContextDto> {
    if (request.trackingUser?.role !== 'PASSENGER') {
      throw new ForbiddenException({
        errorCode: 'TRACKING_ACCESS_DENIED',
        detail: 'Only passengers may access Shuttle passenger context',
      });
    }
    if (!request.shuttleTrackingContext
      || request.shuttleTrackingContext.shuttleTripId !== params.shuttleTripId) {
      throw new ServiceUnavailableException({
        errorCode: 'TRACKING_CONTEXT_UNAVAILABLE',
        detail: 'Shuttle tracking context is unavailable',
      });
    }

    response.setHeader('Cache-Control', 'private, no-store');
    return this.service.getPassengerContext(request.shuttleTrackingContext);
  }

  @Get(':shuttleTripId/operator-context')
  @ApiOperation({ summary: 'Get the owning operator complete Shuttle stop context' })
  @ApiParam({ name: 'shuttleTripId', type: 'string', format: 'uuid' })
  @ApiResponse({ status: 200, description: 'Operator-owned Shuttle stop context.', type: ShuttleOperatorContextEnvelopeSwaggerDto })
  @ApiResponse({ status: 400, description: 'Invalid shuttleTripId.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, description: 'Operator Shuttle tracking access denied.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 404, description: 'Shuttle trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, description: 'Authorization or Shuttle context unavailable.', type: ApiErrorEnvelopeDto })
  operatorContext(
    @Param(new ZodValidationPipe(ShuttleTripIdParamSchema)) params: ShuttleTripIdParamDto,
    @Req() request: AuthorizedShuttleTrackingRequest,
    @Res({ passthrough: true }) response: Response,
  ): ShuttleOperatorContextDto {
    if (request.shuttleTrackingContext?.scope !== 'OPERATOR'
      || request.shuttleTrackingContext.shuttleTripId !== params.shuttleTripId) {
      throw new ForbiddenException({
        errorCode: 'TRACKING_ACCESS_DENIED',
        detail: 'Only the owning operator may access Shuttle operator context',
      });
    }

    response.setHeader('Cache-Control', 'private, no-store');
    return this.service.getOperatorContext(request.shuttleTrackingContext);
  }

  @Get(':shuttleTripId/latest')
  @ApiOperation({ summary: 'Get latest shuttle GPS point' })
  @ApiParam({ name: 'shuttleTripId', type: 'string', format: 'uuid' })
  @ApiResponse({ status: 200, description: 'Latest shuttle GPS point, or null when unavailable.' })
  @ApiResponse({ status: 400, description: 'Invalid shuttleTripId.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({
    status: 403,
    description: 'Shuttle tracking access denied.',
    type: ApiErrorEnvelopeDto,
  })
  @ApiResponse({ status: 404, description: 'Shuttle trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({
    status: 503,
    description: 'Authorization provider unavailable.',
    type: ApiErrorEnvelopeDto,
  })
  async latest(
    @Param(new ZodValidationPipe(ShuttleTripIdParamSchema)) params: ShuttleTripIdParamDto,
  ): Promise<unknown> {
    return this.service.getLatest(params.shuttleTripId);
  }

  @Get(':shuttleTripId/eta')
  @ApiOperation({ summary: 'Get latest shuttle ETA' })
  @ApiParam({ name: 'shuttleTripId', type: 'string', format: 'uuid' })
  @ApiResponse({ status: 200, description: 'Latest shuttle ETA, or null when unavailable.' })
  @ApiResponse({ status: 400, description: 'Invalid shuttleTripId.', type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, description: 'Missing or invalid token.', type: ApiErrorEnvelopeDto })
  @ApiResponse({
    status: 403,
    description: 'Shuttle tracking access denied.',
    type: ApiErrorEnvelopeDto,
  })
  @ApiResponse({ status: 404, description: 'Shuttle trip not found.', type: ApiErrorEnvelopeDto })
  @ApiResponse({
    status: 503,
    description: 'Authorization provider unavailable.',
    type: ApiErrorEnvelopeDto,
  })
  async eta(
    @Param(new ZodValidationPipe(ShuttleTripIdParamSchema)) params: ShuttleTripIdParamDto,
  ): Promise<unknown> {
    return this.service.getEta(params.shuttleTripId);
  }
}
