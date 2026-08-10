import { ApiProperty } from '@nestjs/swagger';

export class TripShareEnvelopeMetaSwaggerDto {
  @ApiProperty({ example: 'req-abc' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-03T16:35:12.000+07:00', format: 'date-time' })
  timestamp!: string;
}
