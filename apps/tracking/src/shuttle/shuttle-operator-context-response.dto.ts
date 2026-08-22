import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';

class ShuttleOperatorStopSwaggerDto {
  @ApiProperty({ example: 3 })
  pickupOrder!: number;

  @ApiProperty({ format: 'uuid', nullable: true })
  bookingId!: string | null;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiProperty({
    enum: ['PENDING', 'PICKED_UP', 'DELIVERED', 'NO_SHOW', 'CANCELLED'],
  })
  status!: string;

  @ApiProperty({ example: false })
  isStation!: boolean;

  @ApiProperty({ example: 2, nullable: true })
  passengerCount!: number | null;

  @ApiProperty({ example: '2026-08-15T10:05:00.000Z', nullable: true })
  pickedUpAt!: string | null;

  @ApiProperty({ example: '2026-08-15T10:25:00.000Z', nullable: true })
  deliveredAt!: string | null;

  @ApiProperty({ example: 'Passenger unavailable', nullable: true })
  statusReason!: string | null;

  @ApiPropertyOptional({ example: '123 Nguyen Hue, Quan 1' })
  serviceAddress?: string;

  @ApiPropertyOptional({ example: 3 })
  serviceOrder?: number;

  @ApiPropertyOptional({ example: 4_200 })
  roadDistanceMeters?: number;
}

class ShuttleOperatorStationSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  stationId!: string;

  @ApiProperty({ example: 'Ben xe Mien Dong' })
  name!: string;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiProperty({ example: 8 })
  pickupOrder!: number;
}

class ShuttleOperatorContextDataSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  shuttleTripId!: string;

  @ApiProperty({ format: 'uuid' })
  mainTripId!: string;

  @ApiProperty({ enum: ['INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION'] })
  direction!: 'INBOUND_TO_STATION' | 'OUTBOUND_FROM_STATION';

  @ApiProperty({ enum: ['SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED'] })
  status!: string;

  @ApiProperty({ type: [ShuttleOperatorStopSwaggerDto] })
  stops!: ShuttleOperatorStopSwaggerDto[];

  @ApiProperty({ type: ShuttleOperatorStationSwaggerDto, nullable: true })
  station!: ShuttleOperatorStationSwaggerDto | null;
}

class ShuttleOperatorContextMetaSwaggerDto {
  @ApiProperty({ example: 'req-a1b2c3d4' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-15T10:00:00.000+07:00' })
  timestamp!: string;
}

export class ShuttleOperatorContextEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: ShuttleOperatorContextDataSwaggerDto })
  data!: ShuttleOperatorContextDataSwaggerDto;

  @ApiProperty({ type: ShuttleOperatorContextMetaSwaggerDto })
  meta!: ShuttleOperatorContextMetaSwaggerDto;
}
