import {
  PARCEL_APPROVAL_REQUESTED_ROUTING_KEY,
  ParcelApprovalRequestedEventSchema,
} from '../../index';

const payload = {
  eventId: '11111111-1111-4111-8111-111111111111',
  occurredAt: '2026-08-31T03:00:00.000Z',
  approvalRequestId: '22222222-2222-4222-8222-222222222222',
  requestType: 'STOP_DEPARTURE',
  operatorId: '33333333-3333-4333-8333-333333333333',
  targetDriverUserId: '44444444-4444-4444-8444-444444444444',
  tripId: '55555555-5555-4555-8555-555555555555',
  parcelId: null,
  incidentId: null,
  stopId: '66666666-6666-4666-8666-666666666666',
  expiresAt: null,
  validityCondition: 'WHILE_STOP_HAS_THE_SAME_UNRESOLVED_SNAPSHOT',
  actionType: 'OPEN_PARCEL_APPROVAL',
  actionParams: {
    requestId: '22222222-2222-4222-8222-222222222222',
    requestType: 'STOP_DEPARTURE',
  },
};

describe('ParcelApprovalRequestedEventSchema', () => {
  it('freezes the routing key and accepts the canonical payload', () => {
    expect(PARCEL_APPROVAL_REQUESTED_ROUTING_KEY).toBe('parcel.approval.requested');
    expect(ParcelApprovalRequestedEventSchema.parse(payload)).toEqual(payload);
  });

  it('rejects unknown fields and mismatched native action parameters', () => {
    expect(ParcelApprovalRequestedEventSchema.safeParse({ ...payload, extra: true }).success)
      .toBe(false);
    expect(ParcelApprovalRequestedEventSchema.safeParse({
      ...payload,
      actionParams: { ...payload.actionParams, requestType: 'CUSTODY_EXCEPTION' },
    }).success).toBe(false);
  });
});
