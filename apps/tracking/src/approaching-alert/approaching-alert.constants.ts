export const APPROACHING_ALERT_EVENT_TYPE = 'ApproachingAlert';
export const APPROACHING_ALERT_WAVE_1 = 'w1';
export const APPROACHING_ALERT_WAVE_2 = 'w2';
export const APPROACHING_ALERT_WAVE_1_THRESHOLD_MINUTES = 30;
export const APPROACHING_ALERT_WAVE_2_THRESHOLD_MINUTES = 10;
export const APPROACHING_ALERT_DEDUPE_TTL_SECONDS = 604_800;

export const BOOKING_DATA_PROVIDER = Symbol('BOOKING_DATA_PROVIDER');

export type ApproachingAlertWave = typeof APPROACHING_ALERT_WAVE_1 | typeof APPROACHING_ALERT_WAVE_2;

export function trackingApproachingNotifiedKey(
  tripId: string,
  bookingId: string,
  wave: ApproachingAlertWave,
): string {
  return `tracking:approaching_notified:${tripId}:${bookingId}:${wave}`;
}
