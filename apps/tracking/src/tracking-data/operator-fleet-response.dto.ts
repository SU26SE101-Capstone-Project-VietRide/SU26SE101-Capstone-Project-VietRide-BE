import { ApiProperty, ApiPropertyOptional, getSchemaPath } from '@nestjs/swagger';

export class TripFleetLatestItemSwaggerDto {
  @ApiProperty({ enum: ['TRIP'] })
  kind!: 'TRIP';

  @ApiProperty({ format: 'uuid' })
  tripId!: string;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiPropertyOptional({ example: 47.5 })
  speedKmh?: number;

  @ApiPropertyOptional({ example: 215 })
  headingDeg?: number;

  @ApiProperty({ example: '2026-08-15T03:00:00.000Z' })
  recordedAt!: string;

  @ApiProperty({ example: 'IN_PROGRESS' })
  status!: string;
}

export class ShuttleFleetLatestItemSwaggerDto {
  @ApiProperty({ enum: ['SHUTTLE'] })
  kind!: 'SHUTTLE';

  @ApiProperty({ format: 'uuid' })
  shuttleTripId!: string;

  @ApiProperty({ format: 'uuid' })
  mainTripId!: string;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiPropertyOptional({ example: 24 })
  speedKmh?: number;

  @ApiPropertyOptional({ example: 120 })
  headingDeg?: number;

  @ApiProperty({ example: '2026-08-15T03:00:00.000Z' })
  recordedAt!: string;

  @ApiProperty({ enum: ['IN_PROGRESS'] })
  status!: 'IN_PROGRESS';
}

class OperatorFleetLatestDataSwaggerDto {
  @ApiProperty({
    type: 'array',
    items: {
      oneOf: [
        { $ref: getSchemaPath(TripFleetLatestItemSwaggerDto) },
        { $ref: getSchemaPath(ShuttleFleetLatestItemSwaggerDto) },
      ],
      discriminator: { propertyName: 'kind' },
    },
  })
  items!: Array<TripFleetLatestItemSwaggerDto | ShuttleFleetLatestItemSwaggerDto>;

  @ApiProperty({ example: '2026-08-15T03:00:01.000Z' })
  generatedAt!: string;
}

class OperatorFleetLatestMetaSwaggerDto {
  @ApiProperty({ example: 'req-a1b2c3d4' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-15T10:00:01.000+07:00' })
  timestamp!: string;
}

export class OperatorFleetLatestEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: OperatorFleetLatestDataSwaggerDto })
  data!: OperatorFleetLatestDataSwaggerDto;

  @ApiProperty({ type: OperatorFleetLatestMetaSwaggerDto })
  meta!: OperatorFleetLatestMetaSwaggerDto;
}
