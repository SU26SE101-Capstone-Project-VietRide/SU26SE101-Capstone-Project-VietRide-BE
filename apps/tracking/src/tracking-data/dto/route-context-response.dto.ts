import { ApiProperty } from '@nestjs/swagger';

export class PublicRoutePointSwaggerDto {
  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;
}

export class PublicRouteGeometrySwaggerDto {
  @ApiProperty({ enum: ['ROUTE_POLYLINE'], example: 'ROUTE_POLYLINE' })
  source!: 'ROUTE_POLYLINE';

  @ApiProperty({ type: [PublicRoutePointSwaggerDto] })
  points!: PublicRoutePointSwaggerDto[];
}

export class PublicRouteStationSwaggerDto extends PublicRoutePointSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  stationId!: string;

  @ApiProperty({ example: 'Ben xe Mien Dong' })
  name!: string;
}

export class PublicRouteIntermediateStopSwaggerDto extends PublicRoutePointSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  stopId!: string;

  @ApiProperty({ example: 'Tram Thu Duc' })
  name!: string;

  @ApiProperty({ example: 1 })
  sequence!: number;
}

class PublicTripRouteContextDataSwaggerDto {
  @ApiProperty({ format: 'uuid' })
  tripId!: string;

  @ApiProperty({ required: false, example: 'IN_PROGRESS' })
  tripStatus?: string;

  @ApiProperty({ type: PublicRouteGeometrySwaggerDto, nullable: true })
  geometry!: PublicRouteGeometrySwaggerDto | null;

  @ApiProperty({ type: PublicRouteStationSwaggerDto, nullable: true })
  originStation!: PublicRouteStationSwaggerDto | null;

  @ApiProperty({ type: [PublicRouteIntermediateStopSwaggerDto] })
  intermediateStops!: PublicRouteIntermediateStopSwaggerDto[];

  @ApiProperty({ type: PublicRouteStationSwaggerDto, nullable: true })
  destinationStation!: PublicRouteStationSwaggerDto | null;
}

class ApiMetaSwaggerDto {
  @ApiProperty({ example: 'req-a1b2c3d4' })
  traceId!: string;

  @ApiProperty({ example: '2026-08-02T19:00:00.000+07:00' })
  timestamp!: string;
}

export class PublicTripRouteContextEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: PublicTripRouteContextDataSwaggerDto })
  data!: PublicTripRouteContextDataSwaggerDto;

  @ApiProperty({ type: ApiMetaSwaggerDto })
  meta!: ApiMetaSwaggerDto;
}
