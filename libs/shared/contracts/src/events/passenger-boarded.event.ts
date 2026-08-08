import { z } from 'zod';

export const PassengerBoardedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    bookingCode: z.string().trim().min(1),
    tripId: z.string().uuid(),
    passengerRecordId: z.string().uuid(),
    seatNumber: z.string().trim().min(1),
    ticketCode: z.string().trim().min(1),
    boardedAt: z.string().datetime({ offset: true }),
  })
  .strict();

export type PassengerBoardedEvent = z.infer<typeof PassengerBoardedEventSchema>;
export const PASSENGER_BOARDED_ROUTING_KEY = 'booking.passenger.boarded';
