import { z } from 'zod';

export const CreateOperatorAnnouncementSchema = z
  .object({
    scope: z.enum(['TRIP', 'OPERATOR']),
    tripId: z.string().uuid().optional(),
    title: z.string().trim().min(1).max(120),
    body: z.string().trim().min(1).max(500),
  })
  .superRefine((value, ctx) => {
    if (value.scope === 'TRIP' && !value.tripId) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['tripId'],
        message: 'tripId is required for TRIP scope',
      });
    }
    if (value.scope === 'OPERATOR' && value.tripId) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['tripId'],
        message: 'tripId is only allowed for TRIP scope',
      });
    }
  });

export type CreateOperatorAnnouncementDto = z.infer<typeof CreateOperatorAnnouncementSchema>;
