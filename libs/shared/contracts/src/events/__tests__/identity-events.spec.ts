import {
  IDENTITY_OPERATOR_APPROVED_ROUTING_KEY,
  IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY,
  IDENTITY_OTP_REQUESTED_ROUTING_KEY,
  IDENTITY_USER_CREATED_ROUTING_KEY,
  IdentityOperatorApprovedEventSchema,
  IdentityOperatorSuspendedEventSchema,
  IdentityOtpRequestedEventSchema,
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
    expect(IDENTITY_OTP_REQUESTED_ROUTING_KEY).toBe('identity.otp.requested');
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

  it('accepts a well-formed identity.otp.requested payload', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      email: 'rider@example.com',
      code: '123456',
      purpose: 'REGISTRATION',
      ttlMinutes: 5,
    });
    expect(result.success).toBe(true);
  });

  it('accepts PASSWORD_RESET purpose on identity.otp.requested', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      email: 'rider@example.com',
      code: '654321',
      purpose: 'PASSWORD_RESET',
      ttlMinutes: 10,
    });
    expect(result.success).toBe(true);
  });

  it('rejects an invalid purpose on identity.otp.requested', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      email: 'rider@example.com',
      code: '123456',
      purpose: 'UNKNOWN_PURPOSE',
      ttlMinutes: 5,
    });
    expect(result.success).toBe(false);
  });

  it('rejects a non-uuid userId on identity.otp.requested', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: 'not-a-uuid',
      email: 'rider@example.com',
      code: '123456',
      purpose: 'REGISTRATION',
      ttlMinutes: 5,
    });
    expect(result.success).toBe(false);
  });

  it('rejects a non-positive ttlMinutes on identity.otp.requested', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      email: 'rider@example.com',
      code: '123456',
      purpose: 'REGISTRATION',
      ttlMinutes: 0,
    });
    expect(result.success).toBe(false);
  });

  it('rejects an invalid email on identity.otp.requested', () => {
    const result = IdentityOtpRequestedEventSchema.safeParse({
      userId: '11111111-1111-1111-1111-111111111111',
      email: 'not-an-email',
      code: '123456',
      purpose: 'REGISTRATION',
      ttlMinutes: 5,
    });
    expect(result.success).toBe(false);
  });
});
