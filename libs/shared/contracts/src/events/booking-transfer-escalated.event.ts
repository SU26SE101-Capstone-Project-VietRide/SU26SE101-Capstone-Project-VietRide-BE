import { z } from 'zod';

export const BookingTransferEscalatedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    bookingId: z.string().uuid(),
    bookingCode: z.string().trim().min(1),
    operatorId: z.string().uuid(),
    oldTripId: z.string().uuid(),
    newTripId: z.string().uuid(),
    transferIds: z.array(z.string().uuid()).min(1),
    pendingConfirmationCount: z.number().int().positive(),
    oldestTransferredAt: z.string().datetime({ offset: true }),
  })
  .strict()
  .superRefine((event, context) => {
    if (event.pendingConfirmationCount !== event.transferIds.length) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['pendingConfirmationCount'],
        message: 'must equal transferIds.length',
      });
    }
  });

export type BookingTransferEscalatedEvent = z.infer<
  typeof BookingTransferEscalatedEventSchema
>;

export const BOOKING_TRANSFER_ESCALATED_ROUTING_KEY =
  'booking.booking.transfer_escalated';
