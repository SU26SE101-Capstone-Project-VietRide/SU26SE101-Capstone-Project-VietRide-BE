import { z } from 'zod';

export const TripSettlementCompletedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  settlementId: z.string().uuid(),
  tripId: z.string().uuid(),
  operatorId: z.string().uuid(),
  netAmount: z.number().int().positive(),
  settlementMethod: z.enum(['AUTO_WEEKLY', 'ADMIN_MANUAL']),
  settledAt: z.string().datetime({ offset: true }),
});
export type TripSettlementCompletedEvent = z.infer<typeof TripSettlementCompletedEventSchema>;
export const TRIP_SETTLEMENT_COMPLETED_ROUTING_KEY = 'payment.trip_settlement.completed';
