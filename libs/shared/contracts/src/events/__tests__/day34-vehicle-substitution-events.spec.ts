import {
  BOOKING_TRANSFERRED_ROUTING_KEY,
  BookingTransferredEventSchema,
  TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY,
  TripVehicleSubstitutedEventSchema,
} from '../../index';

const eventId = '34000000-0000-4000-8000-000000000001';
const occurredAt = '2026-07-25T10:00:00+07:00';
const bookingId = '34000000-0000-4000-8000-000000000002';
const passengerId = '34000000-0000-4000-8000-000000000003';
const operatorId = '34000000-0000-4000-8000-000000000004';
const oldTripId = '34000000-0000-4000-8000-000000000005';
const oldVehicleId = '34000000-0000-4000-8000-000000000006';
const newTripId = '34000000-0000-4000-8000-000000000007';
const newVehicleId = '34000000-0000-4000-8000-000000000008';
const actorUserId = '34000000-0000-4000-8000-000000000009';
const recipientUserId = '34000000-0000-4000-8000-000000000010';

const substitutedEvent = {
  eventId,
  occurredAt,
  substitutionId: eventId,
  disruptedAt: occurredAt,
  operatorId,
  oldTripId,
  oldTripStatus: 'DISRUPTED' as const,
  oldVehicleId,
  newTripId,
  newTripStatus: 'BOARDING' as const,
  newVehicleId,
  newVehiclePlateNumber: '51B-123.45',
  newTripDepartureDateTime: '2026-07-25T03:30:00Z',
  actorUserId,
  reason: 'Engine failure',
  notifyPassengers: true,
  mappings: [
    {
      bookingId,
      passengerId,
      originalSeatNumber: null,
      newSeatNumber: null,
      originalBoardingStatus: 'BOARDED' as const,
    },
  ],
};

const transferredEvent = {
  eventId: '34000000-0000-4000-8000-000000000011',
  occurredAt: '2026-07-25T03:01:00Z',
  sourceSubstitutionEventId: eventId,
  bookingId,
  recipientUserId,
  operatorId,
  oldTripId,
  newTripId,
  newVehicleId,
  newVehiclePlateNumber: '51B-123.45',
  newTripDepartureDateTime: '2026-07-25T10:30:00+07:00',
  notifyPassengers: false,
  transfers: [
    {
      passengerId,
      originalSeatNumber: null,
      newSeatNumber: null,
      confirmationStatus: 'PENDING_CONFIRM' as const,
    },
  ],
};

describe('Day 34 vehicle substitution event contracts', () => {
  it('exports canonical routing keys and accepts exact chained-substitution payloads', () => {
    expect(TRIP_VEHICLE_SUBSTITUTED_ROUTING_KEY).toBe('trip.trip.vehicle_substituted');
    expect(BOOKING_TRANSFERRED_ROUTING_KEY).toBe('booking.booking.transferred');
    expect(TripVehicleSubstitutedEventSchema.parse(substitutedEvent)).toEqual(substitutedEvent);
    expect(BookingTransferredEventSchema.parse(transferredEvent)).toEqual(transferredEvent);
  });

  it('enforces substitution identity, timestamps, status literals, UUIDs, and nullable seat fields', () => {
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        substitutionId: actorUserId,
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        occurredAt: '2026-07-25T03:00:00Z',
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        oldTripStatus: 'IN_PROGRESS',
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        newTripStatus: 'SCHEDULED',
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        operatorId: 'not-a-uuid',
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        mappings: [
          {
            ...substitutedEvent.mappings[0],
            originalSeatNumber: undefined,
          },
        ],
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        mappings: [
          {
            ...substitutedEvent.mappings[0],
            newSeatNumber: undefined,
          },
        ],
      }).success,
    ).toBe(false);
  });

  it('rejects missing, extra, legacy, PII, and invalid boarding fields', () => {
    const missingField: Partial<typeof substitutedEvent> = { ...substitutedEvent };
    delete missingField.reason;

    expect(TripVehicleSubstitutedEventSchema.safeParse(missingField).success).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        tripId: oldTripId,
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        passengerName: 'Passenger PII',
      }).success,
    ).toBe(false);
    expect(
      TripVehicleSubstitutedEventSchema.safeParse({
        ...substitutedEvent,
        mappings: [
          {
            ...substitutedEvent.mappings[0],
            originalBoardingStatus: 'NO_SHOW',
          },
        ],
      }).success,
    ).toBe(false);
  });

  it('enforces the exact booking recipient, transfer shape, and confirmation enum', () => {
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        recipientUserIds: [recipientUserId],
      }).success,
    ).toBe(false);
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        passengerUserId: recipientUserId,
      }).success,
    ).toBe(false);
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        passengerEmail: 'passenger@example.com',
      }).success,
    ).toBe(false);
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        transfers: [
          {
            ...transferredEvent.transfers[0],
            confirmationStatus: 'PENDING',
          },
        ],
      }).success,
    ).toBe(false);
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        transfers: [
          {
            ...transferredEvent.transfers[0],
            originalSeatNumber: undefined,
          },
        ],
      }).success,
    ).toBe(false);
    expect(
      BookingTransferredEventSchema.safeParse({
        ...transferredEvent,
        transfers: [
          {
            ...transferredEvent.transfers[0],
            newSeatNumber: 12,
          },
        ],
      }).success,
    ).toBe(false);
  });
});
