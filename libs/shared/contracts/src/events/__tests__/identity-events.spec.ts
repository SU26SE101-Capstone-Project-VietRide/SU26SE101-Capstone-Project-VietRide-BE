import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
  IdentityOperatorApprovedEventSchema,
  IdentityOperatorSuspendedEventSchema,
  IdentityUserCreatedEventSchema,
} from '../identity-events';

/**
 * Wire-format contract tests for the Identity integration events.
 *
 * Fixtures mirror the bare JSON payloads Identity publishes to the
 * `vietride.events` topic exchange (no eventId/occurredAt envelope).
 */
describe('Identity integration event contracts', () => {
  it('binds the published routing keys', () => {
    expect(IDENTITY_USER_CREATED_ROUTING_KEY).toBe('identity.user.created');
    expect(IDENTITY_OPERATOR_APPROVED_ROUTING_KEY).toBe('identity.operator.approved');
    expect(IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY).toBe('identity.operator.suspended');
  });

  it('accepts a well-formed identity.user.created payload', () => {
    const result = IdentityUserCreatedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      role: 'PASSENGER',
      email: 'rider@example.com',
      createdAt: '2026-06-10T08:30:00+07:00',
    });
    expect(result.success).toBe(true);
  });

  it('rejects a lowercase role on identity.user.created', () => {
    const result = IdentityUserCreatedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      role: 'passenger',
      email: 'rider@example.com',
      createdAt: '2026-06-10T08:30:00+07:00',
    });
    expect(result.success).toBe(false);
  });

  it('accepts a well-formed identity.operator.approved payload', () => {
    const result = IdentityOperatorApprovedEventSchema.safeParse({
      operatorId: '22222222-2222-2222-2222-222222222222',
      approvedAt: '2026-06-10T08:30:00+07:00',
    });
    expect(result.success).toBe(true);
  });

  it('accepts a well-formed identity.operator.suspended payload', () => {
    const result = IdentityOperatorSuspendedEventSchema.safeParse({
      operatorId: '22222222-2222-2222-2222-222222222222',
      suspendedAt: '2026-06-10T08:30:00+07:00',
    });
    expect(result.success).toBe(true);
  });

  it('rejects a non-uuid operatorId', () => {
    const result = IdentityOperatorSuspendedEventSchema.safeParse({
      operatorId: 'not-a-uuid',
      suspendedAt: '2026-06-10T08:30:00+07:00',
    });
    expect(result.success).toBe(false);
  });
});
