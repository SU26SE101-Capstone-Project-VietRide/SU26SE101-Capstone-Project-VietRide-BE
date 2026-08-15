import { Controller, ForbiddenException, Get, Query, Req, UseGuards } from '@nestjs/common';
import { z } from 'zod';
import type { Request } from 'express';
import { ZodValidationPipe } from '@vietride/nest-common';
import {
  ApiBearerAuth,
  ApiExtraModels,
  ApiOperation,
  ApiQuery,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import type { TrackingUser } from '../auth/tracking-user.types';
import { ApiErrorEnvelopeDto } from './dto/swagger-response.dto';
import { OperatorFleetAuthGuard } from './operator-fleet-auth.guard';
import {
  OperatorFleetLatestEnvelopeSwaggerDto,
  ShuttleFleetLatestItemSwaggerDto,
  TripFleetLatestItemSwaggerDto,
} from './operator-fleet-response.dto';
import { OperatorFleetService } from './operator-fleet.service';

const FleetQuerySchema = z.object({
  status: z.enum(['SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'DISRUPTED']).optional(),
  include: z.literal('shuttle').optional(),
});

@ApiTags('Operator Tracking')
@ApiBearerAuth()
@ApiExtraModels(TripFleetLatestItemSwaggerDto, ShuttleFleetLatestItemSwaggerDto)
@Controller('/v1/tracking/operator')
@UseGuards(OperatorFleetAuthGuard)
export class OperatorFleetController {
  constructor(private readonly fleet: OperatorFleetService) {}

  @Get('fleet-latest')
  @ApiOperation({ summary: 'Get the operator latest main Trip and optional active Shuttle GPS' })
  @ApiQuery({ name: 'status', required: false, enum: ['SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'DISRUPTED'] })
  @ApiQuery({ name: 'include', required: false, enum: ['shuttle'] })
  @ApiResponse({ status: 200, type: OperatorFleetLatestEnvelopeSwaggerDto })
  @ApiResponse({ status: 400, type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 401, type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 403, type: ApiErrorEnvelopeDto })
  @ApiResponse({ status: 503, type: ApiErrorEnvelopeDto })
  getLatest(
    @Req() request: Request & { user: TrackingUser },
    @Query(new ZodValidationPipe(FleetQuerySchema)) query: z.infer<typeof FleetQuerySchema>,
  ) {
    const operatorId = request.user.operatorId;
    if (!operatorId) throw new ForbiddenException({ errorCode: 'FORBIDDEN' });
    return this.fleet.getLatest(operatorId, query.status, query.include === 'shuttle');
  }
}
