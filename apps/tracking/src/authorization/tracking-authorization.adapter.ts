import { Injectable } from '@nestjs/common';
import type { TrackingUser } from '../auth/tracking-user.types';

export type TrackingScope =
  | 'BOOKING_OWNER'
  | 'DRIVER'
  | 'ASSISTANT'
  | 'OPERATOR'
  | 'PARCEL_SENDER'
  | 'PARCEL_RECIPIENT';

export interface TrackingAuthorizationResult {
  allowed: boolean;
  scope?: TrackingScope;
  error?: 'TRIP_NOT_FOUND' | 'ACCESS_DENIED' | 'TRACKING_TRIP_NOT_ACTIVE' | 'TRACKING_AUTH_UNAVAILABLE';
}

export interface TrackingAuthorizationAdapter {
  authorizeTripTracking(user: TrackingUser, tripId: string): Promise<TrackingAuthorizationResult>;
}

@Injectable()
export class MvpTrackingAuthorizationAdapter implements TrackingAuthorizationAdapter {
  async authorizeTripTracking(user: TrackingUser, tripId: string): Promise<TrackingAuthorizationResult> {
    void tripId;
    const scope = this.resolveScope(user.role);
    if (!scope) {
      return { allowed: false, error: 'ACCESS_DENIED' };
    }

    return { allowed: true, scope };
  }

  private resolveScope(role: string): TrackingScope | undefined {
    switch (role) {
      case 'PASSENGER':
        return 'BOOKING_OWNER';
      case 'DRIVER':
        return 'DRIVER';
      case 'ASSISTANT':
        return 'ASSISTANT';
      case 'OPERATOR_ADMIN':
      case 'OPERATOR_STAFF':
        return 'OPERATOR';
      default:
        return undefined;
    }
  }
}
