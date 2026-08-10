import type { Namespace } from 'socket.io';
import {
  TripShareRealtimePublisher,
  type TripShareEtaSource,
} from './trip-share-realtime.publisher';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const GRANT_ID = '22222222-2222-4222-8222-222222222222';

describe('TripShareRealtimePublisher', () => {
  it('is an idempotent no-op before a namespace is attached', async () => {
    const publisher = new TripShareRealtimePublisher();

    expect(() =>
      publisher.publishGps({
        tripId: TRIP_ID,
        latitude: 10.7,
        longitude: 106.6,
        speedKmh: 42,
        headingDeg: 90,
        recordedAt: '2026-08-03T10:00:00.000Z',
      }),
    ).not.toThrow();
    await expect(publisher.revokeGrant(GRANT_ID, 'REVOKED')).resolves.toBeUndefined();
  });

  it('publishes exact allow-listed GPS, ETA and status payloads to the trip room', () => {
    const fixture = createNamespaceFixture();
    const publisher = new TripShareRealtimePublisher();
    publisher.attach(fixture.namespace);

    const gpsSource = {
      tripId: TRIP_ID,
      latitude: 10.7,
      longitude: 106.6,
      speedKmh: 42,
      headingDeg: 90,
      recordedAt: '2026-08-03T10:00:00.000Z',
      forbidden: 'never-emitted',
    };
    const etaSource: TripShareEtaSource = {
      tripId: TRIP_ID,
      estimatedArrivalTime: '2026-08-03T11:00:00.000Z',
      etaMinutes: 60,
      delayStatus: 'DELAYED',
      delayMinutes: 35,
      updatedAt: '2026-08-03T10:00:01.000Z',
    };
    const statusSource = {
      tripId: TRIP_ID,
      stopId: 'private-stop',
      status: 'DELAYED',
      delayMinutes: 35,
      updatedAt: '2026-08-03T10:00:01.000Z',
    };
    publisher.publishGps(gpsSource);
    publisher.publishEta(etaSource);
    publisher.publishStatus(statusSource);

    expect(fixture.to).toHaveBeenCalledTimes(3);
    expect(fixture.to).toHaveBeenCalledWith(`shared-trip:${TRIP_ID}`);
    expect(fixture.emit).toHaveBeenNthCalledWith(1, 'shared:gps:update', {
      location: {
        latitude: 10.7,
        longitude: 106.6,
        heading: 90,
        speedKph: 42,
        recordedAt: '2026-08-03T17:00:00.000+07:00',
      },
    });
    expect(fixture.emit).toHaveBeenNthCalledWith(2, 'shared:eta:update', {
      eta: {
        estimatedArrivalAt: '2026-08-03T18:00:00.000+07:00',
        remainingSeconds: 3_600,
        delayMinutes: 35,
        delayStatus: 'DELAYED',
        updatedAt: '2026-08-03T17:00:01.000+07:00',
      },
    });
    expect(fixture.emit).toHaveBeenNthCalledWith(3, 'shared:trip:statusChanged', {
      status: 'DELAYED',
      delayMinutes: 35,
      updatedAt: '2026-08-03T17:00:01.000+07:00',
    });
    expect(gpsSource.recordedAt).toBe('2026-08-03T10:00:00.000Z');
    expect(etaSource.estimatedArrivalTime).toBe('2026-08-03T11:00:00.000Z');
    expect(statusSource.updatedAt).toBe('2026-08-03T10:00:01.000Z');
    expect(JSON.stringify(fixture.emit.mock.calls)).not.toMatch(/tripId|stopId|forbidden/);
  });

  it('emits access revocation before disconnecting only the selected grant room', async () => {
    const grantSocket = { disconnect: jest.fn() };
    const otherSocket = { disconnect: jest.fn() };
    const fixture = createNamespaceFixture({
      [`shared-grant:${GRANT_ID}`]: [grantSocket],
      'shared-grant:other': [otherSocket],
    });
    const publisher = new TripShareRealtimePublisher();
    publisher.attach(fixture.namespace);

    await publisher.revokeGrant(GRANT_ID, 'REVOKED');

    expect(fixture.operations).toEqual([
      `emit:shared-grant:${GRANT_ID}:shared:access:revoked`,
      `fetch:shared-grant:${GRANT_ID}`,
      `disconnect:shared-grant:${GRANT_ID}`,
    ]);
    expect(grantSocket.disconnect).toHaveBeenCalledWith(true);
    expect(otherSocket.disconnect).not.toHaveBeenCalled();
  });

  it('revokes every viewer in a trip room and tolerates an already-empty room', async () => {
    const first = { disconnect: jest.fn() };
    const second = { disconnect: jest.fn() };
    const fixture = createNamespaceFixture({
      [`shared-trip:${TRIP_ID}`]: [first, second],
    });
    const publisher = new TripShareRealtimePublisher();
    publisher.attach(fixture.namespace);

    await publisher.revokeTrip(TRIP_ID, 'TRIP_ENDED');
    await publisher.revokeTrip('33333333-3333-4333-8333-333333333333', 'TRIP_ENDED');

    expect(first.disconnect).toHaveBeenCalledWith(true);
    expect(second.disconnect).toHaveBeenCalledWith(true);
  });
});

function createNamespaceFixture(
  roomSockets: Record<string, Array<{ disconnect: jest.Mock }>> = {},
): {
  namespace: Namespace;
  to: jest.Mock;
  emit: jest.Mock;
  operations: string[];
} {
  const operations: string[] = [];
  const emit = jest.fn();
  const to = jest.fn((room: string) => ({
    emit: (event: string, payload: unknown) => {
      operations.push(`emit:${room}:${event}`);
      return emit(event, payload);
    },
    fetchSockets: async () => {
      operations.push(`fetch:${room}`);
      return (roomSockets[room] ?? []).map((socket) => ({
        disconnect: (close: boolean) => {
          operations.push(`disconnect:${room}`);
          return socket.disconnect(close);
        },
      }));
    },
  }));
  return { namespace: { to, in: to } as unknown as Namespace, to, emit, operations };
}
