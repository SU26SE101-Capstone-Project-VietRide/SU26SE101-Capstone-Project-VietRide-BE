import { ApiProperty } from '@nestjs/swagger';
import { EtaResponseDataDto } from './eta-response.dto';

class ApiMetaDto {
  @ApiProperty({ example: 'req-a1b2c3d4' })
  traceId!: string;

  @ApiProperty({ example: '2026-06-19T12:00:00.000Z' })
  timestamp!: string;
}

class TrackingLatestPointDto {
  @ApiProperty({ example: '11111111-1111-4111-8111-111111111111' })
  tripId!: string;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiProperty({ required: false, example: 42 })
  speedKmh?: number;

  @ApiProperty({ required: false, example: 90 })
  headingDeg?: number;

  @ApiProperty({ example: '2026-06-19T10:00:00.000Z' })
  recordedAt!: string;
}

class LatestDataDto {
  @ApiProperty({ type: TrackingLatestPointDto, nullable: true })
  latest!: TrackingLatestPointDto | null;
}

class TrackingTrailPointDto {
  @ApiProperty({ example: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890' })
  id!: string;

  @ApiProperty({ example: '11111111-1111-4111-8111-111111111111' })
  tripId!: string;

  @ApiProperty({ example: 10.762622 })
  latitude!: number;

  @ApiProperty({ example: 106.660172 })
  longitude!: number;

  @ApiProperty({ required: false, example: 42 })
  speedKmh?: number;

  @ApiProperty({ required: false, example: 90 })
  headingDeg?: number;

  @ApiProperty({ example: '2026-06-19T10:00:00.000Z' })
  recordedAt!: string;
}

class TrailDataDto {
  @ApiProperty({ type: [TrackingTrailPointDto] })
  items!: TrackingTrailPointDto[];

  @ApiProperty({ example: 1 })
  page!: number;

  @ApiProperty({ example: 20 })
  pageSize!: number;

  @ApiProperty({ example: 2 })
  totalItems!: number;

  @ApiProperty({ example: 1 })
  totalPages!: number;

  @ApiProperty({ example: false })
  hasNextPage!: boolean;

  @ApiProperty({ example: false })
  hasPreviousPage!: boolean;
}

class EtaDataDto {
  @ApiProperty({ type: EtaResponseDataDto, nullable: true })
  eta!: EtaResponseDataDto | null;
}

export class TrackingLatestEnvelopeDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: LatestDataDto })
  data!: LatestDataDto;

  @ApiProperty({ type: ApiMetaDto })
  meta!: ApiMetaDto;
}

export class TrackingTrailEnvelopeDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: TrailDataDto })
  data!: TrailDataDto;

  @ApiProperty({ type: ApiMetaDto })
  meta!: ApiMetaDto;
}

export class TrackingEtaEnvelopeDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({ type: EtaDataDto })
  data!: EtaDataDto;

  @ApiProperty({ type: ApiMetaDto })
  meta!: ApiMetaDto;
}

class ApiFieldErrorDto {
  @ApiProperty({ example: 'tripId' })
  field!: string;

  @ApiProperty({ example: 'tripId must be a valid UUID' })
  message!: string;
}

class ApiErrorBodyDto {
  @ApiProperty({ example: 'TRIP_NOT_FOUND' })
  code!: string;

  @ApiProperty({ example: 'Trip 11111111-1111-4111-8111-111111111111 not found' })
  message!: string;

  @ApiProperty({ type: [ApiFieldErrorDto], required: false })
  fields?: ApiFieldErrorDto[];
}

export class ApiErrorEnvelopeDto {
  @ApiProperty({ example: false })
  success!: boolean;

  @ApiProperty({ example: 404 })
  statusCode!: number;

  @ApiProperty({ type: ApiErrorBodyDto })
  error!: ApiErrorBodyDto;

  @ApiProperty({ type: ApiMetaDto })
  meta!: ApiMetaDto;
}
