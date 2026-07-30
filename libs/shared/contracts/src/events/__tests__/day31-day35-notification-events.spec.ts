import {
  BOOKING_DISRUPTED_ROUTING_KEY,
  BookingDisruptedEventSchema,
  PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY,
  PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY,
  PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY,
  ParcelDeliveredPendingConfirmEventSchema,
  ParcelDeliveryConfirmationRealertedEventSchema,
  ParcelPendingOperatorActionRealertedEventSchema,
} from '../../index';

const EVENT_ID = '11111111-1111-4111-8111-111111111111';
const PARCEL_ID = '22222222-2222-4222-8222-222222222222';
const BOOKING_ID = '33333333-3333-4333-8333-333333333333';
const TRIP_ID = '44444444-4444-4444-8444-444444444444';
const OPERATOR_ID = '55555555-5555-4555-8555-555555555555';
const USER_ID = '66666666-6666-4666-8666-666666666666';
const OCCURRED_AT = '2026-07-30T03:00:00Z';

describe('Days 31 and 35 notification event contracts', () => {
  it('accepts delivered-pending-confirm without recipient token metadata', () => {
    expect(
      ParcelDeliveredPendingConfirmEventSchema.parse({
        eventId: EVENT_ID,
        occurredAt: OCCURRED_AT,
        parcelId: PARCEL_ID,
        parcelCode: 'VR-PCL-20260730-ABCDEFGH',
        operatorId: OPERATOR_ID,
        tripId: TRIP_ID,
      }),
    ).toEqual(
      expect.objectContaining({
        eventId: EVENT_ID,
        parcelId: PARCEL_ID,
      }),
    );
  });

  it.each(['deliveryToken', 'deliveryUrl'])(
    'rejects forbidden delivered-pending-confirm field %s',
    (field) => {
      expect(() =>
        ParcelDeliveredPendingConfirmEventSchema.parse({
          eventId: EVENT_ID,
          occurredAt: OCCURRED_AT,
          parcelId: PARCEL_ID,
          parcelCode: 'VR-PCL-20260730-ABCDEFGH',
          operatorId: OPERATOR_ID,
          tripId: TRIP_ID,
          userId: USER_ID,
          recipientUserIds: [USER_ID],
          expiresAt: '2026-08-01T03:00:00Z',
          [field]: 'forbidden-secret',
        }),
      ).toThrow();
    },
  );

  it('accepts exact operator re-alert facts and rejects unexpected fields', () => {
    const deliveryRealert = {
      eventId: EVENT_ID,
      occurredAt: OCCURRED_AT,
      parcelId: PARCEL_ID,
      parcelCode: 'VR-PCL-20260730-ABCDEFGH',
      operatorId: OPERATOR_ID,
      tripId: TRIP_ID,
      expiredAt: '2026-07-23T03:00:00Z',
    };
    const pendingActionRealert = {
      eventId: EVENT_ID,
      occurredAt: OCCURRED_AT,
      parcelId: PARCEL_ID,
      parcelCode: 'VR-PCL-20260730-ABCDEFGH',
      operatorId: OPERATOR_ID,
      userId: USER_ID,
      tripId: TRIP_ID,
    };

    expect(ParcelDeliveryConfirmationRealertedEventSchema.parse(deliveryRealert)).toEqual(
      deliveryRealert,
    );
    expect(ParcelPendingOperatorActionRealertedEventSchema.parse(pendingActionRealert)).toEqual(
      pendingActionRealert,
    );
    expect(() =>
      ParcelDeliveryConfirmationRealertedEventSchema.parse({
        ...deliveryRealert,
        deliveryToken: 'forbidden-secret',
      }),
    ).toThrow();
  });

  it('accepts the exact booking disruption fact and bounds traveledRatio', () => {
    const payload = {
      eventId: EVENT_ID,
      occurredAt: OCCURRED_AT,
      bookingId: BOOKING_ID,
      bookingCode: 'VR-20260730-ABCDEFGH',
      tripId: TRIP_ID,
      operatorId: OPERATOR_ID,
      userId: USER_ID,
      traveledRatio: 0.4,
      refundAmount: 300_000,
      cancellationReason: 'OPERATOR_DISRUPTED_IN_PROGRESS',
    };

    expect(BookingDisruptedEventSchema.parse(payload)).toEqual(payload);
    expect(() =>
      BookingDisruptedEventSchema.parse({ ...payload, traveledRatio: 1.01 }),
    ).toThrow();
  });

  it('exports only canonical routing keys', () => {
    expect(PARCEL_DELIVERED_PENDING_CONFIRM_ROUTING_KEY).toBe(
      'parcel.parcel.delivered_pending_confirm',
    );
    expect(PARCEL_DELIVERY_CONFIRMATION_REALERTED_ROUTING_KEY).toBe(
      'parcel.parcel.delivery_confirmation_realerted',
    );
    expect(PARCEL_PENDING_OPERATOR_ACTION_REALERTED_ROUTING_KEY).toBe(
      'parcel.parcel.pending_operator_action_realerted',
    );
    expect(BOOKING_DISRUPTED_ROUTING_KEY).toBe('booking.booking.disrupted');
  });
});
