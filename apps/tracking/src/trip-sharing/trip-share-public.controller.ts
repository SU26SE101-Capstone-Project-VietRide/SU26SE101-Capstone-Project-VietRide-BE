import { Controller, Get, Header, Req, UseGuards } from '@nestjs/common';
import { ApiHeader, ApiOperation, ApiResponse, ApiTags } from '@nestjs/swagger';
import type { TripShareContextDto } from './trip-share-context.dto';
import { TripShareContextEnvelopeSwaggerDto } from './trip-share-context-envelope.swagger.dto';
import { TripShareContextService } from './trip-share-context.service';
import { TripShareErrorEnvelopeSwaggerDto } from './trip-share-error-envelope.swagger.dto';
import {
  TripShareTokenGuard,
  type AuthorizedTripShareRequest,
} from './trip-share-token.guard';

@ApiTags('Trip Sharing')
@Controller('/v1/tracking/shared-trip')
export class TripSharePublicController {
  constructor(private readonly service: TripShareContextService) {}

  @Get('context')
  @UseGuards(TripShareTokenGuard)
  @Header('Cache-Control', 'no-store')
  @Header('Pragma', 'no-cache')
  @Header('Referrer-Policy', 'no-referrer')
  @ApiOperation({ summary: 'Get an anonymous, privacy-safe snapshot of an active shared Trip' })
  @ApiHeader({
    name: 'X-Trip-Share-Token',
    required: true,
    schema: { type: 'string' },
    description: 'Passenger-issued Trip sharing capability token',
  })
  @ApiResponse({ status: 200, type: TripShareContextEnvelopeSwaggerDto })
  @ApiResponse({ status: 401, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 410, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 429, type: TripShareErrorEnvelopeSwaggerDto })
  @ApiResponse({ status: 503, type: TripShareErrorEnvelopeSwaggerDto })
  getContext(@Req() request: AuthorizedTripShareRequest): Promise<TripShareContextDto> {
    return this.service.getContext(request.tripShareAccess);
  }
}
