import { z } from 'zod';

export const TripShareOwnerParamSchema = z.object({
  tripId: z.string().uuid(),
});

export type TripShareOwnerParamDto = z.infer<typeof TripShareOwnerParamSchema>;

export interface TripShareLinkResponseDto {
  shareUrl: string;
  expiresAt: string;
}

export interface TripShareRevokedResponseDto {
  revoked: true;
}
