import { ApiProperty } from '@nestjs/swagger';
import type { TripShareContextDto } from './trip-share-context.dto';
import { TripShareEnvelopeMetaSwaggerDto } from './trip-share-envelope-meta.swagger.dto';

export class TripShareContextEnvelopeSwaggerDto {
  @ApiProperty({ example: true })
  success!: boolean;

  @ApiProperty({ example: 200 })
  statusCode!: number;

  @ApiProperty({
    type: 'object',
    properties: {
      status: {
        type: 'string',
        enum: ['IN_PROGRESS', 'VEHICLE_REPLACEMENT_PENDING'],
      },
      expiresAt: { type: 'string', format: 'date-time' },
      lastUpdatedAt: { type: 'string', format: 'date-time', nullable: true },
      vehicle: {
        type: 'object',
        properties: {
          location: {
            type: 'object',
            nullable: true,
            properties: {
              latitude: { type: 'number' },
              longitude: { type: 'number' },
              heading: { type: 'number', nullable: true },
              speedKph: { type: 'number', nullable: true },
              recordedAt: { type: 'string', format: 'date-time' },
            },
          },
        },
      },
      route: {
        type: 'object',
        properties: {
          originName: { type: 'string' },
          destinationName: { type: 'string' },
          origin: {
            type: 'object',
            nullable: true,
            properties: {
              latitude: { type: 'number', minimum: -90, maximum: 90 },
              longitude: { type: 'number', minimum: -180, maximum: 180 },
            },
            required: ['latitude', 'longitude'],
            additionalProperties: false,
          },
          destination: {
            type: 'object',
            nullable: true,
            properties: {
              latitude: { type: 'number', minimum: -90, maximum: 90 },
              longitude: { type: 'number', minimum: -180, maximum: 180 },
            },
            required: ['latitude', 'longitude'],
            additionalProperties: false,
          },
          stops: {
            type: 'array',
            maxItems: 100,
            items: {
              type: 'object',
              properties: {
                name: { type: 'string' },
                latitude: { type: 'number', minimum: -90, maximum: 90 },
                longitude: { type: 'number', minimum: -180, maximum: 180 },
                sequence: { type: 'integer', minimum: 1 },
              },
              required: ['name', 'latitude', 'longitude', 'sequence'],
              additionalProperties: false,
            },
          },
          geometry: {
            type: 'object',
            nullable: true,
            properties: {
              type: { type: 'string', enum: ['LineString'] },
              coordinates: {
                type: 'array',
                items: { type: 'array', items: { type: 'number' }, minItems: 2, maxItems: 2 },
              },
            },
          },
        },
      },
      eta: {
        type: 'object',
        nullable: true,
        properties: {
          estimatedArrivalAt: { type: 'string', format: 'date-time' },
          remainingSeconds: { type: 'integer' },
          delayMinutes: { type: 'integer', nullable: true },
          updatedAt: { type: 'string', format: 'date-time' },
        },
      },
    },
  })
  data!: TripShareContextDto;

  @ApiProperty({ type: TripShareEnvelopeMetaSwaggerDto })
  meta!: TripShareEnvelopeMetaSwaggerDto;
}
