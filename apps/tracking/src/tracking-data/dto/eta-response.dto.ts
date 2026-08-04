import { ApiProperty } from '@nestjs/swagger';
import { z } from 'zod';

export const EtaBaseResponseSchema = z.object({
  tripId: z.string().uuid(),
  stopId: z.string().uuid(),
  etaMinutes: z.number().int().positive(),
  estimatedArrivalTime: z.string().datetime(),
  distanceMeters: z.number().int().nonnegative(),
  updatedAt: z.string().datetime(),
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

  @ApiProperty({ example: '22222222-2222-4222-8222-222222222222' })
  stopId!: string;

  @ApiProperty({ example: 12 })
  etaMinutes!: number;

  @ApiProperty({ example: '2026-06-19T12:30:00.000Z' })
  estimatedArrivalTime!: string;

  @ApiProperty({ example: 8500 })
  distanceMeters!: number;

  @ApiProperty({ example: '2026-06-19T12:01:00.000Z' })
  updatedAt!: string;

  @ApiProperty({ nullable: true, example: true })
  delayed!: boolean | null;

  @ApiProperty({ enum: ['DELAYED', 'ON_TIME', 'UNKNOWN'], example: 'DELAYED' })
  delayStatus!: 'DELAYED' | 'ON_TIME' | 'UNKNOWN';

  @ApiProperty({ nullable: true, example: 31 })
  delayMinutes!: number | null;
}
