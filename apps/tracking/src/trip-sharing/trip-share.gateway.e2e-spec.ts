import { INestApplication, ServiceUnavailableException } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { io, type Socket } from 'socket.io-client';
import type { Env } from '../config/env.schema';
import { ENV_TOKEN } from '../app/tokens';
import { TRACKING_SOCKET_PATH } from '../location/location.constants';
import { TripShareAccessService } from './trip-share-access.service';
import { TripShareGateway } from './trip-share.gateway';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareRateLimiter } from './trip-share-rate-limiter';
import {
  SHARED_ACCESS_REVOKED_EVENT,
  SHARED_GPS_UPDATE_EVENT,
  TRIP_SHARE_SOCKET_NAMESPACE,
  sharedGrantRoom,
  sharedTripRoom,
} from './trip-share-realtime.constants';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const GRANT_A = '22222222-2222-4222-8222-222222222222';
const GRANT_B = '33333333-3333-4333-8333-333333333333';
const USER_ID = '44444444-4444-4444-8444-444444444444';
const WAIT_MS = 2_000;

describe('TripShareGateway (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let publisher: TripShareRealtimePublisher;
  let codec: TripShareTokenCodec;
  let tokenA: string;
  let tokenB: string;
  const grants = new Map<string, Record<string, unknown>>();
  const repository = {
    findById: jest.fn(async (id: string) => grants.get(id) ?? null),
    revokeGrantById: jest.fn(async (id: string, reason: string, now: Date) => {
      const grant = grants.get(id);
      if (!grant || grant.revokedAt) return 0;
      grant.revokedAt = now;
      grant.revokeReason = reason;
      return 1;
    }),
  };
  const rateLimiter = { consume: jest.fn(async () => undefined) };
  const trips = { getTrip: jest.fn(async () => ({ tripId: TRIP_ID, status: 'IN_PROGRESS' })) };

  beforeAll(async () => {
    const env = createTestEnv();
    const moduleRef = await Test.createTestingModule({
      providers: [
        TripShareGateway,
        TripShareRealtimePublisher,
        TripShareAccessService,
        TripShareTokenCodec,
        { provide: ENV_TOKEN, useValue: env },
        { provide: TripShareGrantRepository, useValue: repository },
        { provide: TripShareRateLimiter, useValue: rateLimiter },
        { provide: TripShareTripSnapshotProvider, useValue: trips },
      ],
    }).compile();
    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
    publisher = moduleRef.get(TripShareRealtimePublisher);
    codec = moduleRef.get(TripShareTokenCodec);
  });

  beforeEach(() => {
    jest.clearAllMocks();
    grants.clear();
    trips.getTrip.mockResolvedValue({ tripId: TRIP_ID, status: 'IN_PROGRESS' });
    ({ token: tokenA } = addGrant(GRANT_A));
    ({ token: tokenB } = addGrant(GRANT_B));
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('accepts only auth.shareToken and joins server-owned trip and grant rooms', async () => {
    const socket = await connectShared({ shareToken: tokenA });

    expect(socket.connected).toBe(true);
    expect(rateLimiter.consume).toHaveBeenCalledWith('socket', tokenA);
    expect(serverRoomMembers(sharedTripRoom(TRIP_ID))).toContain(socket.id);
    expect(serverRoomMembers(sharedGrantRoom(GRANT_A))).toContain(socket.id);

    socket.emit('joinTripTracking', { tripId: 'attacker-selected-trip' });
    await delay(25);
    expect(serverRoomMembers('shared-trip:attacker-selected-trip')).toEqual([]);
    socket.disconnect();
  });

  it('rejects missing, malformed and Identity-style auth.token handshakes with safe codes', async () => {
    await expect(connectShared({})).rejects.toThrow('TRACKING_SHARE_TOKEN_INVALID');
    await expect(connectShared({ shareToken: 'malformed' })).rejects.toThrow('TRACKING_SHARE_TOKEN_INVALID');
    await expect(connectShared({ token: tokenA })).rejects.toThrow('TRACKING_SHARE_TOKEN_INVALID');
  });

  it('publishes one sanitized GPS event to each viewer without internal identifiers', async () => {
    const first = await connectShared({ shareToken: tokenA });
    const second = await connectShared({ shareToken: tokenA });
    const firstEvent = waitForEvent<Record<string, unknown>>(first, SHARED_GPS_UPDATE_EVENT);
    const secondEvent = waitForEvent<Record<string, unknown>>(second, SHARED_GPS_UPDATE_EVENT);

    publisher.publishGps({
      tripId: TRIP_ID,
      latitude: 10.7,
      longitude: 106.6,
      speedKmh: 42,
      headingDeg: 90,
      recordedAt: '2026-08-03T10:00:00.000Z',
    });

    const events = await Promise.all([firstEvent, secondEvent]);
    expect(events).toEqual([
      { location: { latitude: 10.7, longitude: 106.6, speedKph: 42, heading: 90, recordedAt: '2026-08-03T10:00:00.000Z' } },
      { location: { latitude: 10.7, longitude: 106.6, speedKph: 42, heading: 90, recordedAt: '2026-08-03T10:00:00.000Z' } },
    ]);
    expect(JSON.stringify(events)).not.toMatch(/tripId|grantId|userId/);
    first.disconnect();
    second.disconnect();
  });

  it('revokes grant A after the access event while grant B remains connected', async () => {
    const first = await connectShared({ shareToken: tokenA });
    const second = await connectShared({ shareToken: tokenB });
    const operations: string[] = [];
    first.once(SHARED_ACCESS_REVOKED_EVENT, () => operations.push('event'));
    first.once('disconnect', () => operations.push('disconnect'));

    await publisher.revokeGrant(GRANT_A, 'REVOKED');
    await waitForCondition(() => !first.connected);

    expect(operations).toEqual(['event', 'disconnect']);
    expect(second.connected).toBe(true);
    second.disconnect();
  });

  it('revokes every grant in a trip room', async () => {
    const first = await connectShared({ shareToken: tokenA });
    const second = await connectShared({ shareToken: tokenB });

    await publisher.revokeTrip(TRIP_ID, 'TRIP_ENDED');
    await waitForCondition(() => !first.connected && !second.connected);

    expect(first.connected).toBe(false);
    expect(second.connected).toBe(false);
  });

  it('emits EXPIRED then disconnects at the exact grant expiry', async () => {
    const expiring = addGrant(GRANT_A, new Date(Date.now() + 150));
    const socket = await connectShared({ shareToken: expiring.token });
    const revoked = waitForEvent<{ reason: string }>(socket, SHARED_ACCESS_REVOKED_EVENT);

    await expect(revoked).resolves.toEqual({ reason: 'EXPIRED' });
    await waitForCondition(() => !socket.connected);
  });

  it('periodically disconnects terminal and unavailable access without consuming rate quota again', async () => {
    const terminalSocket = await connectShared({ shareToken: tokenA });
    const initialRateCalls = rateLimiter.consume.mock.calls.length;
    trips.getTrip.mockResolvedValue({ tripId: TRIP_ID, status: 'COMPLETED' });
    const terminal = waitForEvent<{ reason: string }>(terminalSocket, SHARED_ACCESS_REVOKED_EVENT);
    await expect(terminal).resolves.toEqual({ reason: 'TRIP_ENDED' });

    grants.set(GRANT_A, createGrant(GRANT_A, codec.create(GRANT_A).tokenHash));
    trips.getTrip.mockResolvedValue({ tripId: TRIP_ID, status: 'IN_PROGRESS' });
    const unavailableSocket = await connectShared({ shareToken: tokenA });
    trips.getTrip.mockRejectedValue(new ServiceUnavailableException());
    const unavailable = waitForEvent<{ reason: string }>(unavailableSocket, SHARED_ACCESS_REVOKED_EVENT);
    await expect(unavailable).resolves.toEqual({ reason: 'ACCESS_UNAVAILABLE' });

    expect(rateLimiter.consume.mock.calls.length).toBe(initialRateCalls + 1);
  });

  it('denies reconnect after the backing grant is revoked', async () => {
    const grant = grants.get(GRANT_A);
    if (!grant) throw new Error('grant fixture missing');
    grant.revokedAt = new Date();
    grant.revokeReason = 'USER_REVOKED';

    await expect(connectShared({ shareToken: tokenA })).rejects.toThrow('TRACKING_SHARE_LINK_UNAVAILABLE');
  });

  function addGrant(grantId: string, expiresAt = new Date(Date.now() + 60_000)): { token: string } {
    const created = codec.create(grantId);
    grants.set(grantId, createGrant(grantId, created.tokenHash, expiresAt));
    return { token: created.token };
  }

  function createGrant(grantId: string, tokenHash: string, expiresAt = new Date(Date.now() + 60_000)) {
    return {
      id: grantId,
      tripId: TRIP_ID,
      createdByUserId: USER_ID,
      tokenHash,
      tokenVersion: 1,
      expiresAt,
      revokedAt: null,
      revokeReason: null,
      createdAt: new Date(),
      updatedAt: new Date(),
    };
  }

  function serverRoomMembers(room: string): string[] {
    const namespace = (publisher as unknown as { namespace?: { adapter: { rooms: Map<string, Set<string>> } } }).namespace;
    return [...(namespace?.adapter.rooms.get(room) ?? [])];
  }

  function connectShared(auth: Record<string, string>): Promise<Socket> {
    return connectSocket(`http://127.0.0.1:${port}${TRIP_SHARE_SOCKET_NAMESPACE}`, auth);
  }
});

function connectSocket(url: string, auth: Record<string, string>): Promise<Socket> {
  return new Promise((resolve, reject) => {
    const socket = io(url, {
      path: TRACKING_SOCKET_PATH,
      auth,
      transports: ['websocket'],
      forceNew: true,
      reconnection: false,
      timeout: WAIT_MS,
    });
    const timeout = setTimeout(() => {
      socket.disconnect();
      reject(new Error('SOCKET_CONNECT_TIMEOUT'));
    }, WAIT_MS);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve(socket);
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      socket.disconnect();
      reject(error);
    });
  });
}

function waitForEvent<T>(socket: Socket, event: string): Promise<T> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${event}_TIMEOUT`)), WAIT_MS);
    socket.once(event, (payload: T) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

async function waitForCondition(condition: () => boolean): Promise<void> {
  const deadline = Date.now() + WAIT_MS;
  while (Date.now() < deadline) {
    if (condition()) return;
    await delay(10);
  }
  throw new Error('WAIT_FOR_CONDITION_TIMEOUT');
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function createTestEnv(): Env {
  return {
    TRACKING_SHARE_TOKEN_SECRET: 'phase13-shared-gateway-secret-at-least-32-bytes',
    TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 0.05,
  } as Env;
}

function readListeningPort(app: INestApplication): number {
  const address = (app.getHttpServer() as { address(): string | { port: number } | null }).address();
  if (typeof address === 'object' && address !== null) return address.port;
  throw new Error('TRACKING_SHARE_GATEWAY_E2E_PORT_UNAVAILABLE');
}
