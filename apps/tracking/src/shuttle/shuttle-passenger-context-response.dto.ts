import { ApiProperty } from '@nestjs/swagger';

class ShuttlePassengerPickupSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  bookingId!: string;

  @ApiProperty({ example: 3 })
  pickupOrder!: number;

  @ApiProperty({ example: '123 Nguyen Hue, Quan 1', required: false })
  serviceAddress?: string;

  @ApiProperty({ example: 3, required: false })
  serviceOrder?: number;

  @ApiProperty({ example: 4200, required: false })
  roadDistanceMeters?: number;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiProperty({ enum: ['PENDING', 'PICKED_UP'] })
  status!: 'PENDING' | 'PICKED_UP';

  @ApiProperty({ example: 2 })
  stopsBeforePickup!: number;
}

class ShuttlePassengerStationSwaggerDto {
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

class ShuttlePassengerContextDataSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  shuttleTripId!: string;

  @ApiProperty({ format: 'uuid' })
  mainTripId!: string;

  @ApiProperty({ enum: ['INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION'] })
  direction!: 'INBOUND_TO_STATION' | 'OUTBOUND_FROM_STATION';

  @ApiProperty({ type: [ShuttlePassengerPickupSwaggerDto] })
  ownPickups!: ShuttlePassengerPickupSwaggerDto[];

  @ApiProperty({ type: ShuttlePassengerStationSwaggerDto, nullable: true })
  station!: ShuttlePassengerStationSwaggerDto | null;
}

class ShuttlePassengerContextMetaSwaggerDto {
  @ApiProperty({ example: 'req-a1b2c3d4' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-02T12:00:00.000Z' })
  timestamp!: string;
}

export class ShuttlePassengerContextEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: ShuttlePassengerContextDataSwaggerDto })
  data!: ShuttlePassengerContextDataSwaggerDto;

  @ApiProperty({ type: ShuttlePassengerContextMetaSwaggerDto })
  meta!: ShuttlePassengerContextMetaSwaggerDto;
}
