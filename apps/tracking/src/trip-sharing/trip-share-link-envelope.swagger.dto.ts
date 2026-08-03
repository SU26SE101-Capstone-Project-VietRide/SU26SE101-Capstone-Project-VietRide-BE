import { ApiProperty } from '@nestjs/swagger';
import { TripShareEnvelopeMetaSwaggerDto } from './trip-share-envelope-meta.swagger.dto';

export class TripShareLinkEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({
    type: 'object',
    properties: {
      shareUrl: { type: 'string', example: 'https://app.vietride.vn/trip-sharing#token=v1.xxx.signature' },
      expiresAt: { type: 'string', format: 'date-time' },
    },
  })
  data!: { shareUrl: string; expiresAt: string };

  @ApiProperty({ type: TripShareEnvelopeMetaSwaggerDto })
  meta!: TripShareEnvelopeMetaSwaggerDto;
}
