import { z } from 'zod';

export const PARCEL_APPROVAL_REQUESTED_ROUTING_KEY = 'parcel.approval.requested';

export const ParcelApprovalRequestTypeSchema = z.enum([
  'CUSTODY_EXCEPTION',
  'STOP_DEPARTURE',
]);

export const ParcelApprovalRequestedEventSchema = z
  .object({
    eventId: z.string().uuid(),
    occurredAt: z.string().datetime({ offset: true }),
    approvalRequestId: z.string().uuid(),
    requestType: ParcelApprovalRequestTypeSchema,
    operatorId: z.string().uuid(),
    targetDriverUserId: z.string().uuid(),
    tripId: z.string().uuid(),
    parcelId: z.string().uuid().nullable(),
    incidentId: z.string().uuid().nullable(),
    stopId: z.string().uuid().nullable(),
    expiresAt: z.null(),
    validityCondition: z.string().trim().min(1),
    actionType: z.literal('OPEN_PARCEL_APPROVAL'),
    actionParams: z
      .object({
        requestId: z.string().uuid(),
        requestType: ParcelApprovalRequestTypeSchema,
      })
      .strict(),
  })
  .strict()
  .superRefine((event, context) => {
    if (event.actionParams.requestId !== event.approvalRequestId) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['actionParams', 'requestId'],
        message: 'Action requestId must match approvalRequestId.',
      });
    }
    if (event.actionParams.requestType !== event.requestType) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['actionParams', 'requestType'],
        message: 'Action requestType must match requestType.',
      });
    }
  });

export type ParcelApprovalRequestedEvent = z.infer<
  typeof ParcelApprovalRequestedEventSchema
>;
