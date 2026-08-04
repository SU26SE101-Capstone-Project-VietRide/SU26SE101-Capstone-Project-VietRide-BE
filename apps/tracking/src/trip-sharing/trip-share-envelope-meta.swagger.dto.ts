import { ApiProperty } from '@nestjs/swagger';

export class TripShareEnvelopeMetaSwaggerDto {
  @ApiProperty({ example: 'req-abc' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-03T09:35:12.000Z', format: 'date-time' })
  timestamp!: string;
}
