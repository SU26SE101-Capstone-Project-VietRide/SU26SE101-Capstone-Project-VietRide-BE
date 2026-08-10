import { Injectable } from '@nestjs/common';
import type { Namespace } from 'socket.io';
import { transformFrontendTimestamps } from '@vietride/nest-common';
import {
  SHARED_ACCESS_REVOKED_EVENT,
  SHARED_ETA_UPDATE_EVENT,
  SHARED_GPS_UPDATE_EVENT,
  SHARED_TRIP_STATUS_CHANGED_EVENT,
  sharedGrantRoom,
  sharedTripRoom,
  type TripShareAccessRevocationReason,
} from './trip-share-realtime.constants';

const SECONDS_PER_MINUTE = 60;

export interface TripShareGpsSource {
  tripId: string;
  latitude: number;
  longitude: number;
  speedKmh?: number;
  headingDeg?: number;
  recordedAt: string;
}

export interface TripShareEtaSource {
  tripId: string;
  estimatedArrivalTime: string;
  etaMinutes: number;
  delayStatus?: 'DELAYED' | 'ON_TIME' | 'UNKNOWN';
  delayMinutes?: number | null;
  updatedAt: string;
}

export interface TripShareStatusSource {
  tripId: string;
  status: string;
  delayMinutes?: number | null;
  updatedAt: string;
}

@Injectable()
export class TripShareRealtimePublisher {
  private namespace?: Namespace;

  attach(namespace: Namespace): void {
    this.namespace = namespace;
  }

  publishGps(source: TripShareGpsSource): void {
    this.namespace?.to(sharedTripRoom(source.tripId)).emit(
      SHARED_GPS_UPDATE_EVENT,
      transformFrontendTimestamps({
        location: {
          latitude: source.latitude,
          longitude: source.longitude,
          heading: source.headingDeg ?? null,
          speedKph: source.speedKmh ?? null,
          recordedAt: source.recordedAt,
        },
      }),
    );
  }

  publishEta(source: TripShareEtaSource): void {
    this.namespace?.to(sharedTripRoom(source.tripId)).emit(
      SHARED_ETA_UPDATE_EVENT,
      transformFrontendTimestamps({
        eta: {
          estimatedArrivalAt: source.estimatedArrivalTime,
          remainingSeconds: source.etaMinutes * SECONDS_PER_MINUTE,
          delayMinutes: source.delayMinutes ?? null,
          delayStatus: source.delayStatus ?? 'UNKNOWN',
          updatedAt: source.updatedAt,
        },
      }),
    );
  }

  publishStatus(source: TripShareStatusSource): void {
    this.namespace?.to(sharedTripRoom(source.tripId)).emit(
      SHARED_TRIP_STATUS_CHANGED_EVENT,
      transformFrontendTimestamps({
        status: source.status,
        delayMinutes: source.delayMinutes ?? null,
        updatedAt: source.updatedAt,
      }),
    );
  }

  async revokeGrant(grantId: string, reason: TripShareAccessRevocationReason): Promise<void> {
    await this.revokeRoom(sharedGrantRoom(grantId), reason);
  }

  async revokeTrip(tripId: string, reason: TripShareAccessRevocationReason): Promise<void> {
    await this.revokeRoom(sharedTripRoom(tripId), reason);
  }

  private async revokeRoom(room: string, reason: TripShareAccessRevocationReason): Promise<void> {
    const namespace = this.namespace;
    if (!namespace) return;

    namespace.to(room).emit(SHARED_ACCESS_REVOKED_EVENT, { reason });
    const sockets = await namespace.in(room).fetchSockets();
    for (const socket of sockets) socket.disconnect(true);
  }
}
