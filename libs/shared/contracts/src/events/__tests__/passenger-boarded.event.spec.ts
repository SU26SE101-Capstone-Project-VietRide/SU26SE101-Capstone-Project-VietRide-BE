import { PASSENGER_BOARDED_ROUTING_KEY, PassengerBoardedEventSchema } from '../..';

const event = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-08-08T01:00:00.000Z',
  bookingId: '22222222-2222-4222-8222-222222222222',
  bookingCode: 'VR-20260808-ABCDEFGH',
  tripId: '33333333-3333-4333-8333-333333333333',
  passengerRecordId: '44444444-4444-4444-8444-444444444444',
  seatNumber: 'A01',
  ticketCode: 'VT-20260808-ABCDEFGH',
  boardedAt: '2026-08-08T01:00:00.000Z',
};

describe('PassengerBoardedEventSchema', () => {
  it('accepts only the strict operational boarding fact', () => {
    expect(PASSENGER_BOARDED_ROUTING_KEY).toBe('booking.passenger.boarded');
    expect(PassengerBoardedEventSchema.safeParse(event).success).toBe(true);
    expect(PassengerBoardedEventSchema.safeParse({ ...event, extra: true }).success).toBe(false);
    expect(PassengerBoardedEventSchema.safeParse({ ...event, seatNumber: '' }).success).toBe(false);
  });
});
