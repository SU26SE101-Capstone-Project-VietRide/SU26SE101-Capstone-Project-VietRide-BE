import {
  Controller,
  Delete,
  Headers,
  Param,
  Put,
  Req,
  UseGuards,
} from '@nestjs/common';
import {
  ApiBearerAuth,
  ApiHeader,
  ApiOperation,
  ApiParam,
  ApiResponse,
  ApiTags,
} from '@nestjs/swagger';
import { ZodValidationPipe } from '@vietride/nest-common';
import type {
  TripShareLinkResponseDto,
  TripShareOwnerParamDto,
  TripShareRevokedResponseDto,
} from './trip-share-owner.dto';
import { TripShareOwnerParamSchema } from './trip-share-owner.dto';
import {
  TripShareOwnerJwtGuard,
  type AuthorizedTripShareOwnerRequest,
} from './trip-share-owner-jwt.guard';
import { TripShareOwnerService } from './trip-share-owner.service';
import { TripShareLinkEnvelopeSwaggerDto } from './trip-share-link-envelope.swagger.dto';
import { TripShareRevokedEnvelopeSwaggerDto } from './trip-share-revoked-envelope.swagger.dto';
import { TripShareErrorEnvelopeSwaggerDto } from './trip-share-error-envelope.swagger.dto';

@ApiTags('Trip Sharing')
@ApiBearerAuth()
@UseGuards(TripShareOwnerJwtGuard)
@Controller('/v1/tracking/trips')
export class TripShareOwnerController {
  constructor(private readonly service: TripShareOwnerService) {}

  @Put(':tripId/share-link')
  @ApiOperation({ summary: 'Create or return the passenger-owned active trip share link' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid' })
  @ApiHeader({ name: 'Idempotency-Key', required: true, schema: { type: 'string', format: 'uuid' } })
  @ApiResponse({ status: 200, type: TripShareLinkEnvelopeSwaggerDto })
  @ApiResponse({ status: 400, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 401, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 403, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 404, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 409, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 422, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 429, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 503, type: TripShareErrorEnvelopeSwaggerDto })
  ensureShareLink(
    @Param(new ZodValidationPipe(TripShareOwnerParamSchema)) params: TripShareOwnerParamDto,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
    @Req() request: AuthorizedTripShareOwnerRequest,
  ): Promise<TripShareLinkResponseDto> {
    return this.service.ensureShareLink(
      request.trackingUser.userId,
      params.tripId,
      idempotencyKey,
      request.path,
    );
  }

  @Delete(':tripId/share-link')
  @ApiOperation({ summary: 'Revoke the passenger-owned active trip share link' })
  @ApiParam({ name: 'tripId', type: 'string', format: 'uuid' })
  @ApiHeader({ name: 'Idempotency-Key', required: true, schema: { type: 'string', format: 'uuid' } })
  @ApiResponse({ status: 200, type: TripShareRevokedEnvelopeSwaggerDto })
  @ApiResponse({ status: 400, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 401, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 403, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 409, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 422, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 429, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 503, type: TripShareErrorEnvelopeSwaggerDto })
  revokeShareLink(
    @Param(new ZodValidationPipe(TripShareOwnerParamSchema)) params: TripShareOwnerParamDto,
    @Headers('idempotency-key') idempotencyKey: string | undefined,
    @Req() request: AuthorizedTripShareOwnerRequest,
  ): Promise<TripShareRevokedResponseDto> {
    return this.service.revokeShareLink(
      request.trackingUser.userId,
      params.tripId,
      idempotencyKey,
      request.path,
    );
  }
}
