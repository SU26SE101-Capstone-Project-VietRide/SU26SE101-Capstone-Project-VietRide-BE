import { ApiProperty } from '@nestjs/swagger';
import { TripShareEnvelopeMetaSwaggerDto } from './trip-share-envelope-meta.swagger.dto';

export class TripShareRevokedEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({
    type: 'object',
    properties: { revoked: { type: 'boolean', example: true } },
  })
  data!: { revoked: true };

  @ApiProperty({ type: TripShareEnvelopeMetaSwaggerDto })
  meta!: TripShareEnvelopeMetaSwaggerDto;
}
