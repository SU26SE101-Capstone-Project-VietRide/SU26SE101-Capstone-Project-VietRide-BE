export const SHUTTLE_LATEST_TTL_SECONDS = 300;
export const SHUTTLE_BUFFER_TTL_SECONDS = 86_400;
export const SHUTTLE_GPS_IDEMPOTENCY_TTL_SECONDS = 86_400;
export const SHUTTLE_ETA_TTL_SECONDS = 60;
export const SHUTTLE_ETA_STATE_TTL_SECONDS = 86_400;
export const SHUTTLE_ETA_LOCK_TTL_SECONDS = 10;
export const SHUTTLE_BUFFER_MAX_ITEMS = 1_000;

export const shuttleRoom = (id: string): string => `shuttle:${id}`;
export const shuttleLatestKey = (id: string): string => `tracking:shuttle:latest:${id}`;
export const shuttleBufferKey = (id: string): string => `tracking:shuttle:gps_buffer:${id}`;
export const shuttleGpsIdempotencyKey = (id: string, recordedAt: string): string =>
  `tracking:shuttle:gps_idempotency:${id}:${recordedAt}`;
export const shuttleEtaKey = (id: string, order: number): string =>
  `tracking:shuttle:eta:${id}:${order}`;
export const shuttleEtaStateKey = (id: string): string => `tracking:shuttle:eta_state:${id}`;
export const shuttleEtaLockKey = (id: string, order: number): string =>
  `tracking:shuttle:eta_lock:${id}:${order}`;
