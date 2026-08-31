export const TRIP_SHARE_SOCKET_NAMESPACE = '/shared';

export const SHARED_GPS_UPDATE_EVENT = 'shared:gps:update';
export const SHARED_ETA_UPDATE_EVENT = 'shared:eta:update';
export const SHARED_TRIP_STATUS_CHANGED_EVENT = 'shared:trip:statusChanged';
export const SHARED_TRIP_VEHICLE_SUBSTITUTED_EVENT = 'shared:trip:vehicleSubstituted';
export const SHARED_ACCESS_REVOKED_EVENT = 'shared:access:revoked';

export type TripShareAccessRevocationReason =
  | 'EXPIRED'
  | 'REVOKED'
  | 'TRIP_ENDED'
  | 'ACCESS_UNAVAILABLE';

export function sharedTripRoom(tripId: string): string {
  return `shared-trip:${tripId}`;
}

export function sharedGrantRoom(grantId: string): string {
  return `shared-grant:${grantId}`;
}
