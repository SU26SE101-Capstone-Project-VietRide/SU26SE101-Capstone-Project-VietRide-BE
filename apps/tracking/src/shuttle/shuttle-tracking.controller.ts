import { Controller, Get, Param, UseGuards } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiParam, ApiResponse, ApiTags } from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import { ApiErrorEnvelopeDto } from '../tracking-data/dto/swagger-response.dto';
import { ShuttleTrackingAuthGuard } from './shuttle-tracking-auth.guard';
import { ShuttleTripIdParamSchema, type ShuttleTripIdParamDto } from './shuttle.dto';
import { ShuttleService } from './shuttle.service';

@ApiTags('Shuttle Tracking')
@ApiBearerAuth()
@UseGuards(ShuttleTrackingAuthGuard)
@Controller('v1/tracking/shuttle-trips')
export class ShuttleTrackingController {
  constructor(private readonly service: ShuttleService) {}

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
