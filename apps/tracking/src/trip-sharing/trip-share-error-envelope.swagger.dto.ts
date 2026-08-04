import { ApiProperty } from '@nestjs/swagger';
import { TripShareEnvelopeMetaSwaggerDto } from './trip-share-envelope-meta.swagger.dto';

export class TripShareErrorEnvelopeSwaggerDto {
  @ApiProperty({ example: false })
  success!: boolean;

  @ApiProperty({ example: 403 })
  statusCode!: number;

  @ApiProperty({
    type: 'object',
    properties: {
      code: { type: 'string', example: 'ACCESS_DENIED' },
      message: { type: 'string' },
    },
  })
  error!: { code: string; message: string };

  @ApiProperty({ type: TripShareEnvelopeMetaSwaggerDto })
  meta!: TripShareEnvelopeMetaSwaggerDto;
}
