import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import { io, type Socket } from 'socket.io-client';
import {
  ENV_TOKEN,
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../app/tokens';
import { ApproachingAlertService } from '../approaching-alert/approaching-alert.service';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { MvpTrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import type { Env } from '../config/env.schema';
import { EtaService, type EtaUpdateEvent } from '../eta/eta.service';
import { OffRouteService } from '../off-route/off-route.service';
import { TripDelayService, type TripDelayEtaUpdate } from '../trip-delay/trip-delay.service';
import { LocationGateway } from './location.gateway';
import { TRACKING_SOCKET_PATH, trackingGpsBufferKey, trackingLatestKey } from './location.constants';
import { LocationService } from './location.service';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_USER_ID = '22222222-2222-4222-8222-222222222222';
const TEST_OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const CONNECT_TIMEOUT_MS = 2_000;
const ACK_TIMEOUT_MS = 2_000;
const IDENTITY_ISSUER = 'vietride-identity';
const IDENTITY_AUDIENCE = 'vietride-api';

interface JoinTripTrackingAck {
  success: boolean;
  tripId?: string;
  room?: string;
  scope?: string;
  error?: string;
  message?: string;
}

interface GpsUpdateAck {
  success: boolean;
  error?: string;
  message?: string;
}

interface RedisMultiMock {
  set: jest.MockedFunction<(key: string, value: string, mode: string, ttl: number) => RedisMultiMock>;
  rpush: jest.MockedFunction<(key: string, value: string) => RedisMultiMock>;
  sadd: jest.MockedFunction<(key: string, value: string) => RedisMultiMock>;
  exec: jest.MockedFunction<() => Promise<unknown[]>>;
}

describe('LocationGateway identity-backed realtime (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  let publicKeyPem: string;
  let redisMulti: RedisMultiMock;
  let etaHandleGpsUpdate: jest.MockedFunction<(event: unknown) => Promise<EtaUpdateEvent | null>>;
  let approachingHandleEtaUpdate: jest.MockedFunction<(event: EtaUpdateEvent) => Promise<number>>;
  let offRouteHandleGpsUpdate: jest.MockedFunction<(event: unknown) => Promise<unknown>>;
  let tripDelayHandleEtaUpdate: jest.MockedFunction<(event: EtaUpdateEvent) => Promise<TripDelayEtaUpdate>>;

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    publicKeyPem = await exportSPKI(generated.publicKey);

    redisMulti = createRedisMultiMock();
    etaHandleGpsUpdate = jest.fn(async (event: unknown) => {
      void event;
      return null;
    });
    approachingHandleEtaUpdate = jest.fn(async (event: EtaUpdateEvent) => {
      void event;
      return 0;
    });
    offRouteHandleGpsUpdate = jest.fn(async (event: unknown) => {
      void event;
      return null;
    });
    tripDelayHandleEtaUpdate = jest.fn(async (event: EtaUpdateEvent) => ({
      ...event,
      delayed: false,
    }));
    const redisService = {
      getClient: jest.fn(() => ({
        multi: jest.fn(() => redisMulti),
      })),
    };

    const moduleRef = await Test.createTestingModule({
      providers: [
        LocationGateway,
        LocationService,
        {
          provide: ENV_TOKEN,
          useValue: createTestEnv(publicKeyPem),
        },
        {
          provide: TRACKING_JWT_VERIFIER,
          useClass: JoseUserJwtVerifier,
        },
        {
          provide: TRACKING_AUTHORIZATION_ADAPTER,
          useClass: MvpTrackingAuthorizationAdapter,
        },
        {
          provide: RedisService,
          useValue: redisService,
        },
        {
          provide: EtaService,
          useValue: { handleGpsUpdate: etaHandleGpsUpdate },
        },
        {
          provide: ApproachingAlertService,
          useValue: { handleEtaUpdate: approachingHandleEtaUpdate },
        },
        {
          provide: OffRouteService,
          useValue: { handleGpsUpdate: offRouteHandleGpsUpdate },
        },
        {
          provide: TripDelayService,
          useValue: { handleEtaUpdate: tripDelayHandleEtaUpdate },
        },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  beforeEach(() => {
    resetRedisMultiMock(redisMulti);
    etaHandleGpsUpdate.mockClear();
    etaHandleGpsUpdate.mockResolvedValue(null);
    approachingHandleEtaUpdate.mockClear();
    approachingHandleEtaUpdate.mockResolvedValue(0);
    offRouteHandleGpsUpdate.mockClear();
    offRouteHandleGpsUpdate.mockResolvedValue(null);
    tripDelayHandleEtaUpdate.mockClear();
    tripDelayHandleEtaUpdate.mockImplementation(async (event: EtaUpdateEvent) => ({
      ...event,
      delayed: false,
    }));
  });

  afterAll(async () => {
    if (app) {
      await app.close();
    }
  });

  it('connects with a valid Identity-style RS256 access token', async () => {
    const token = await signIdentityToken('PASSENGER');
    const socket = await connectSocket(token);

    expect(socket.connected).toBe(true);
    socket.disconnect();
  });

  it('rejects an invalid access token with UNAUTHORIZED', async () => {
    await expect(connectSocket('not-a-jwt')).rejects.toThrow('UNAUTHORIZED');
  });

  it('allows a valid passenger token to join trip tracking', async () => {
    const token = await signIdentityToken('PASSENGER');
    const socket = await connectSocket(token);

    const ack = await emitWithAck<JoinTripTrackingAck>(socket, 'joinTripTracking', {
      tripId: TEST_TRIP_ID,
    });

    expect(ack).toEqual({
      success: true,
      tripId: TEST_TRIP_ID,
      room: `trip:${TEST_TRIP_ID}`,
      scope: 'BOOKING_OWNER',
    });
    socket.disconnect();
  });

  it('denies gps:update for passenger tokens', async () => {
    const token = await signIdentityToken('PASSENGER');
    const socket = await connectSocket(token);

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());

    expect(ack).toEqual({ success: false, error: 'ACCESS_DENIED' });
    expect(redisMulti.exec).not.toHaveBeenCalled();
    socket.disconnect();
  });

  it('records gps:update for driver tokens', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const payload = createGpsPayload();

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', payload);

    expect(ack).toEqual({ success: true });
    expect(redisMulti.set).toHaveBeenCalledWith(
      trackingLatestKey(TEST_TRIP_ID),
      expect.any(String),
      'EX',
      expect.any(Number),
    );
    expect(redisMulti.rpush).toHaveBeenCalledWith(trackingGpsBufferKey(TEST_TRIP_ID), expect.any(String));
    expect(redisMulti.sadd).toHaveBeenCalledWith('tracking:active_trips', TEST_TRIP_ID);
    expect(redisMulti.exec).toHaveBeenCalledTimes(1);
    expect(offRouteHandleGpsUpdate).toHaveBeenCalledWith(expect.objectContaining({ tripId: TEST_TRIP_ID }));
    expect(etaHandleGpsUpdate).toHaveBeenCalledWith(expect.objectContaining({ tripId: TEST_TRIP_ID }));
    expect(approachingHandleEtaUpdate).not.toHaveBeenCalled();
    socket.disconnect();
  });

  it('broadcasts eta:update when ETA engine recalculates', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const etaUpdate: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: '44444444-4444-4444-8444-444444444444',
      etaMinutes: 12,
      estimatedArrivalTime: '2026-06-03T10:12:00.000Z',
      distanceMeters: 8_000,
      updatedAt: '2026-06-03T10:00:01.000Z',
    };
    etaHandleGpsUpdate.mockResolvedValue(etaUpdate);

    await emitWithAck<JoinTripTrackingAck>(socket, 'joinTripTracking', {
      tripId: TEST_TRIP_ID,
    });
    const etaPromise = waitForEvent<EtaUpdateEvent>(socket, 'eta:update');
    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());
    const receivedEta = await etaPromise;

    expect(ack).toEqual({ success: true });
    expect(receivedEta).toEqual({ ...etaUpdate, delayed: false });
    expect(approachingHandleEtaUpdate).toHaveBeenCalledWith({ ...etaUpdate, delayed: false });
    socket.disconnect();
  });

  it('broadcasts trip:statusChanged when delayed detection flags ETA update', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const etaUpdate: EtaUpdateEvent = {
      tripId: TEST_TRIP_ID,
      stopId: '44444444-4444-4444-8444-444444444444',
      etaMinutes: 60,
      estimatedArrivalTime: '2026-06-03T11:00:00.000Z',
      distanceMeters: 8_000,
      updatedAt: '2026-06-03T10:00:01.000Z',
    };
    const delayedEtaUpdate: TripDelayEtaUpdate = {
      ...etaUpdate,
      delayed: true,
      delayMinutes: 35,
    };
    etaHandleGpsUpdate.mockResolvedValue(etaUpdate);
    tripDelayHandleEtaUpdate.mockResolvedValue(delayedEtaUpdate);

    await emitWithAck<JoinTripTrackingAck>(socket, 'joinTripTracking', {
      tripId: TEST_TRIP_ID,
    });
    const etaPromise = waitForEvent<TripDelayEtaUpdate>(socket, 'eta:update');
    const statusPromise = waitForEvent<Record<string, unknown>>(socket, 'trip:statusChanged');
    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());
    const receivedEta = await etaPromise;
    const receivedStatus = await statusPromise;

    expect(ack).toEqual({ success: true });
    expect(receivedEta).toEqual(delayedEtaUpdate);
    expect(receivedStatus).toEqual({
      tripId: TEST_TRIP_ID,
      stopId: etaUpdate.stopId,
      status: 'DELAYED',
      delayMinutes: 35,
      updatedAt: etaUpdate.updatedAt,
    });
    expect(approachingHandleEtaUpdate).toHaveBeenCalledWith(delayedEtaUpdate);
    socket.disconnect();
  });

  async function signIdentityToken(role: string, operatorId?: string): Promise<string> {
    const jwt = new SignJWT({
      role,
      email: 'tracking-test@vietride.local',
      ...(operatorId ? { operatorId } : {}),
    })
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'tracking-e2e-key' })
      .setSubject(TEST_USER_ID)
      .setIssuer(IDENTITY_ISSUER)
      .setAudience(IDENTITY_AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('15m');

    return jwt.sign(privateKey);
  }

  function connectSocket(token: string): Promise<Socket> {
    return new Promise((resolve, reject) => {
      const socket = io(`http://127.0.0.1:${port}`, {
        path: TRACKING_SOCKET_PATH,
        auth: { token },
        transports: ['websocket'],
        forceNew: true,
        reconnection: false,
        timeout: CONNECT_TIMEOUT_MS,
      });

      const timeout = setTimeout(() => {
        socket.disconnect();
        reject(new Error('SOCKET_CONNECT_TIMEOUT'));
      }, CONNECT_TIMEOUT_MS);

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
});

function emitWithAck<TAck>(socket: Socket, eventName: string, payload: unknown): Promise<TAck> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(`${eventName.toUpperCase()}_ACK_TIMEOUT`));
    }, ACK_TIMEOUT_MS);

    socket.emit(eventName, payload, (ack: TAck) => {
      clearTimeout(timeout);
      resolve(ack);
    });
  });
}

function waitForEvent<TPayload>(socket: Socket, eventName: string): Promise<TPayload> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error(`${eventName.toUpperCase()}_EVENT_TIMEOUT`));
    }, ACK_TIMEOUT_MS);

    socket.once(eventName, (payload: TPayload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

function createGpsPayload(): Record<string, unknown> {
  return {
    tripId: TEST_TRIP_ID,
    latitude: 10.762622,
    longitude: 106.660172,
    speedKmh: 42,
    headingDeg: 90,
    recordedAt: new Date('2026-06-03T10:00:00.000Z').toISOString(),
  };
}

function createRedisMultiMock(): RedisMultiMock {
  const multi = {} as RedisMultiMock;
  multi.set = jest.fn((key: string, value: string, mode: string, ttl: number) => {
    void key;
    void value;
    void mode;
    void ttl;
    return multi;
  });
  multi.rpush = jest.fn((key: string, value: string) => {
    void key;
    void value;
    return multi;
  });
  multi.sadd = jest.fn((key: string, value: string) => {
    void key;
    void value;
    return multi;
  });
  multi.exec = jest.fn(async () => []);
  return multi;
}

function resetRedisMultiMock(multi: RedisMultiMock): void {
  multi.set.mockClear();
  multi.rpush.mockClear();
  multi.sadd.mockClear();
  multi.exec.mockClear();
}

function createTestEnv(publicKeyPem: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_TTL_SEC: 120,
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    JWT_ISSUER: IDENTITY_ISSUER,
    JWT_AUDIENCE: IDENTITY_AUDIENCE,
    REDIS_URL: 'redis://localhost:6379',
    REDIS_HOST: 'localhost',
    REDIS_PORT: 6379,
    RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
    RABBITMQ_EXCHANGE: 'vietride.events',
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
    LOG_LEVEL: 'info',
    USER_JWT_PUBLIC_KEY: publicKeyPem,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
  };
}

function readListeningPort(app: INestApplication): number {
  const address = app.getHttpServer().address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('TRACKING_E2E_PORT_UNAVAILABLE');
}
