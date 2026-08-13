import { z } from 'zod';

/**
 * Identity integration events.
 *
 * Identity publishes these to the `vietride.events` topic exchange with the
 * routing key equal to the message type and the body being the bare JSON
 * payload (no eventId/occurredAt envelope). Schemas below match that wire
 * format EXACTLY (camelCase keys, UPPERCASE role enum, datetime with offset).
 */

export const UserRole = z.enum([
  'PASSENGER',
  'DRIVER',
  'OPERATOR_ADMIN',
  'OPERATOR_STAFF',
  'SYSTEM_ADMIN',
]);
export type UserRole = z.infer<typeof UserRole>;

export const IdentityUserCreatedEventSchema = z.object({
  userId: z.string().uuid(),
  role: UserRole,
  email: z.string().email(),
  createdAt: z.string().datetime({ offset: true }),
});
export type IdentityUserCreatedEvent = z.infer<typeof IdentityUserCreatedEventSchema>;

export const IdentityOperatorApprovedEventSchema = z.object({
  eventId: z.string().uuid(),
  operatorId: z.string().uuid(),
  approvedAt: z.string().datetime({ offset: true }),
});
export type IdentityOperatorApprovedEvent = z.infer<typeof IdentityOperatorApprovedEventSchema>;

export const IdentityOperatorRejectedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    operatorId: z.string().uuid(),
    companyName: z.string().trim().min(1),
    contactEmail: z.string().email(),
    reason: z.string().trim().min(1),
  })
  .strict();
export type IdentityOperatorRejectedEvent = z.infer<
  typeof IdentityOperatorRejectedEventSchema
>;

export const IdentityOperatorSuspendedEventSchema = z.object({
  operatorId: z.string().uuid(),
  suspendedAt: z.string().datetime({ offset: true }),
});
export type IdentityOperatorSuspendedEvent = z.infer<typeof IdentityOperatorSuspendedEventSchema>;

export const IdentityOtpRequestedEventSchema = z.object({
  userId: z.string().uuid(),
  email: z.string().email(),
  code: z.string().length(6),
  purpose: z.enum(['REGISTRATION', 'PASSWORD_RESET']),
  ttlMinutes: z.number().int().positive(),
});
export type IdentityOtpRequestedEvent = z.infer<typeof IdentityOtpRequestedEventSchema>;

// <service>.<aggregate>.<verb_past> per BACKEND_SOURCE_OF_TRUTH §7.3.
export const IDENTITY_USER_CREATED_ROUTING_KEY = 'identity.user.created';
export const IDENTITY_OPERATOR_APPROVED_ROUTING_KEY = 'identity.operator.approved';
export const IDENTITY_OPERATOR_REJECTED_ROUTING_KEY = 'identity.operator.rejected';
export const IDENTITY_OPERATOR_SUSPENDED_ROUTING_KEY = 'identity.operator.suspended';
export const IDENTITY_OTP_REQUESTED_ROUTING_KEY = 'identity.otp.requested';
