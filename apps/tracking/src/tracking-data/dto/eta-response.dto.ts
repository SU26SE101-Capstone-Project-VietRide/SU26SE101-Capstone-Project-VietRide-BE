import { ApiProperty } from '@nestjs/swagger';
import { z } from 'zod';

export const EtaBaseResponseSchema = z.object({
  tripId: z.string().uuid(),
  targetKind: z.enum(['STOP', 'STATION']).default('STOP'),
  stopId: z.string().uuid().optional(),
  stationId: z.string().uuid().optional(),
  stopName: z.string().nullable().optional(),
  etaMinutes: z.number().int().positive(),
  estimatedArrivalTime: z.string().datetime(),
  distanceMeters: z.number().int().nonnegative(),
  updatedAt: z.string().datetime(),
  sequence: z.number().int().positive().optional(),
  estimateQuality: z.enum(['TRAFFIC_AWARE', 'FALLBACK']).default('FALLBACK'),
});

export const EtaResponseSchema = EtaBaseResponseSchema.extend({
  delayed: z.boolean().nullable().default(null),
  delayStatus: z.enum(['DELAYED', 'ON_TIME', 'UNKNOWN']),
  delayMinutes: z.number().int().nonnegative().nullable(),
});

export type EtaResponseDto = z.infer<typeof EtaResponseSchema>;

export class EtaResponseDataDto {
  @ApiProperty({ example: '11111111-1111-4111-8111-111111111111' })
  tripId!: string;

  @ApiProperty({ enum: ['STOP', 'STATION'], example: 'STOP' })
  targetKind!: 'STOP' | 'STATION';

  @ApiProperty({ required: false, example: '22222222-2222-4222-8222-222222222222' })
  stopId?: string;

  @ApiProperty({ required: false, example: '33333333-3333-4333-8333-333333333333' })
  stationId?: string;

  @ApiProperty({ nullable: true, example: 'Bến xe Miền Tây' })
  stopName!: string | null;

  @ApiProperty({ example: 12 })
  etaMinutes!: number;

  @ApiProperty({ example: '2026-06-19T12:30:00.000Z' })
  estimatedArrivalTime!: string;

  @ApiProperty({ example: 8500 })
  distanceMeters!: number;

  @ApiProperty({ example: '2026-06-19T12:01:00.000Z' })
  updatedAt!: string;

  @ApiProperty({ required: false, example: 1 })
  sequence?: number;

  @ApiProperty({ enum: ['TRAFFIC_AWARE', 'FALLBACK'], example: 'TRAFFIC_AWARE' })
  estimateQuality!: 'TRAFFIC_AWARE' | 'FALLBACK';

  @ApiProperty({ nullable: true, example: true })
  delayed!: boolean | null;

  @ApiProperty({ enum: ['DELAYED', 'ON_TIME', 'UNKNOWN'], example: 'DELAYED' })
  delayStatus!: 'DELAYED' | 'ON_TIME' | 'UNKNOWN';

  @ApiProperty({ nullable: true, example: 31 })
  delayMinutes!: number | null;
}
