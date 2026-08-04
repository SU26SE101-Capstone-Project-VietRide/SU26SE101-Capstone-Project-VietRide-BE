import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import { io, type Socket } from 'socket.io-client';
import { ENV_TOKEN, TRACKING_AUTHORIZATION_ADAPTER, TRACKING_JWT_VERIFIER } from '../app/tokens';
import { ApproachingAlertService } from '../approaching-alert/approaching-alert.service';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import { MvpTrackingAuthorizationAdapter } from '../authorization/tracking-authorization.adapter';
import type { Env } from '../config/env.schema';
import { EtaService, type EtaUpdateEvent } from '../eta/eta.service';
import { OffRouteService } from '../off-route/off-route.service';
import { ROUTE_GEOMETRY_PROVIDER } from '../off-route/off-route.constants';
import { ShuttleService } from '../shuttle/shuttle.service';
import { ShuttleEtaService } from '../shuttle/shuttle-eta.service';
import type { ShuttleEtaEvent } from '../shuttle/shuttle-eta.service';
import { TripDelayService, type TripDelayEtaUpdate } from '../trip-delay/trip-delay.service';
import { TripShareRealtimePublisher } from '../trip-sharing/trip-share-realtime.publisher';
import { LocationGateway } from './location.gateway';
import {
  TRACKING_SOCKET_PATH,
  trackingGpsIdempotencyKey,
} from './location.constants';
import { LocationService, type GpsUpdateEvent } from './location.service';

const TEST_TRIP_ID = '11111111-1111-4111-8111-111111111111';
const TEST_USER_ID = '22222222-2222-4222-8222-222222222222';
const TEST_OPERATOR_ID = '33333333-3333-4333-8333-333333333333';
const TEST_SHUTTLE_ID = '55555555-5555-4555-8555-555555555555';
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

describe('LocationGateway identity-backed realtime (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  let publicKeyPem: string;
  let redisEval: jest.MockedFunction<(...args: unknown[]) => Promise<number>>;
  let etaHandleGpsUpdate: jest.MockedFunction<(event: unknown) => Promise<EtaUpdateEvent | null>>;
  let approachingHandleEtaUpdate: jest.MockedFunction<(event: EtaUpdateEvent) => Promise<number>>;
  let offRouteHandleGpsUpdate: jest.MockedFunction<(event: unknown) => Promise<unknown>>;
  let tripDelayHandleEtaUpdate: jest.MockedFunction<
    (event: EtaUpdateEvent) => Promise<TripDelayEtaUpdate>
  >;
  let routePeek: jest.Mock;
  let shuttleGetContext: jest.Mock;
  let shuttleRecordLocation: jest.Mock;
  let shuttleEtaHandleGpsUpdate: jest.Mock;
  let sharedPublishGps: jest.Mock;
  let sharedPublishEta: jest.Mock;
  let sharedPublishStatus: jest.Mock;

  beforeAll(async () => {
    const generated = await generateKeyPair('RS256');
    privateKey = generated.privateKey;
    publicKeyPem = await exportSPKI(generated.publicKey);

    redisEval = jest.fn(async () => 1);
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
    routePeek = jest.fn(() => null);
    shuttleGetContext = jest.fn();
    shuttleRecordLocation = jest.fn();
    shuttleEtaHandleGpsUpdate = jest.fn(async () => undefined);
    sharedPublishGps = jest.fn();
    sharedPublishEta = jest.fn();
    sharedPublishStatus = jest.fn();
    const redisService = {
      getClient: jest.fn(() => ({
        eval: redisEval,
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
          provide: ROUTE_GEOMETRY_PROVIDER,
          useValue: {
            peekCachedRouteGeometry: routePeek,
            getRouteGeometry: async () => null,
          },
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
        {
          provide: ShuttleService,
          useValue: {
            getContext: shuttleGetContext,
            recordLocation: shuttleRecordLocation,
          },
        },
        {
          provide: ShuttleEtaService,
          useValue: { handleGpsUpdate: shuttleEtaHandleGpsUpdate },
        },
        {
          provide: TripShareRealtimePublisher,
          useValue: {
            publishGps: sharedPublishGps,
            publishEta: sharedPublishEta,
            publishStatus: sharedPublishStatus,
          },
        },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  beforeEach(() => {
    redisEval.mockClear();
    redisEval.mockResolvedValue(1);
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
    routePeek.mockReset();
    routePeek.mockReturnValue(null);
    shuttleGetContext.mockReset();
    shuttleRecordLocation.mockReset();
    shuttleEtaHandleGpsUpdate.mockReset();
    shuttleEtaHandleGpsUpdate.mockResolvedValue(undefined);
    sharedPublishGps.mockReset();
    sharedPublishEta.mockReset();
    sharedPublishStatus.mockReset();
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

  it('does not accept a share token as authentication on the default namespace', async () => {
    await expect(connectWithAuth({ shareToken: 'v1.share.signature' })).rejects.toThrow('UNAUTHORIZED');
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
    expect(redisEval).not.toHaveBeenCalled();
    socket.disconnect();
  });

  it('records gps:update for driver tokens', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const payload = createGpsPayload();

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', payload);

    expect(ack).toEqual({ success: true });
    expect(redisEval).toHaveBeenCalledWith(
      expect.any(String),
      4,
      trackingGpsIdempotencyKey(TEST_TRIP_ID, payload.recordedAt as string),
      expect.any(String),
      expect.any(String),
      'tracking:active_trips',
      expect.stringMatching(/^[a-f0-9]{64}$/),
      expect.any(String),
      expect.any(String),
      '86400',
      '300',
      TEST_TRIP_ID,
    );
    await waitForCondition(() => offRouteHandleGpsUpdate.mock.calls.length > 0);
    await waitForCondition(() => etaHandleGpsUpdate.mock.calls.length > 0);
    expect(offRouteHandleGpsUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ tripId: TEST_TRIP_ID }),
    );
    expect(etaHandleGpsUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ tripId: TEST_TRIP_ID }),
    );
    expect(sharedPublishGps).toHaveBeenCalledTimes(1);
    expect(sharedPublishGps).toHaveBeenCalledWith(expect.objectContaining({
      tripId: TEST_TRIP_ID,
      latitude: payload.latitude,
      longitude: payload.longitude,
      speedKmh: payload.speedKmh,
      headingDeg: payload.headingDeg,
      recordedAt: payload.recordedAt,
    }));
    expect(approachingHandleEtaUpdate).not.toHaveBeenCalled();
    socket.disconnect();
  });

  it('returns ack before the detection chain completes', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    let releaseDetection: () => void = () => undefined;
    const detectionStarted = new Promise<void>((resolve) => {
      offRouteHandleGpsUpdate.mockImplementationOnce(async () => {
        resolve();
        await new Promise<void>((release) => {
          releaseDetection = release;
        });
        return null;
      });
    });

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());

    expect(ack).toEqual({ success: true });
    await detectionStarted;
    releaseDetection();
    await waitForCondition(() => etaHandleGpsUpdate.mock.calls.length > 0);
    socket.disconnect();
  });

  it('broadcasts Shuttle GPS and returns ack before Google ETA completes', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const context = {
      shuttleTripId: TEST_SHUTTLE_ID,
      mainTripId: TEST_TRIP_ID,
      operatorId: TEST_OPERATOR_ID,
      driverUserId: TEST_USER_ID,
      allowed: true,
      scope: 'DRIVER',
      stops: [{
        pickupOrder: 1,
        bookingId: null,
        latitude: 10.8,
        longitude: 106.7,
        status: 'PENDING',
        isStation: true,
      }],
    };
    const payload = {
      shuttleTripId: TEST_SHUTTLE_ID,
      latitude: 10.7,
      longitude: 106.65,
      speedKmh: 30,
      recordedAt: '2026-08-01T01:00:00.000Z',
    };
    const eta: ShuttleEtaEvent = {
      shuttleTripId: TEST_SHUTTLE_ID,
      nextPickupOrder: 1,
      etaMinutes: 10,
      estimatedArrivalTime: '2026-08-01T01:10:00.000Z',
      distanceMeters: 6_200,
      updatedAt: '2026-08-01T01:00:01.000Z',
    };
    let releaseEta: () => void = () => undefined;
    shuttleGetContext.mockResolvedValue(context);
    shuttleRecordLocation.mockResolvedValue({ gps: payload, duplicate: false });
    shuttleEtaHandleGpsUpdate.mockImplementationOnce(
      () => new Promise<ShuttleEtaEvent>((resolve) => {
        releaseEta = () => resolve(eta);
      }),
    );

    await emitWithAck<JoinTripTrackingAck>(socket, 'joinShuttleTracking', {
      shuttleTripId: TEST_SHUTTLE_ID,
    });
    const gpsPromise = waitForEvent<typeof payload>(socket, 'shuttle:gps:update');
    const etaPromise = waitForEvent<ShuttleEtaEvent>(socket, 'shuttle:eta:update');
    const ack = await emitWithAck<GpsUpdateAck>(socket, 'shuttle:gps:update', payload);
    const receivedGps = await gpsPromise;

    expect(ack).toEqual({ success: true });
    expect(receivedGps).toEqual(payload);
    expect(shuttleEtaHandleGpsUpdate).toHaveBeenCalledWith(payload, context);

    releaseEta();
    await expect(etaPromise).resolves.toEqual(eta);
    socket.disconnect();
  });

  it('re-authorizes passenger Shuttle access on every join and reconnect', async () => {
    const token = await signIdentityToken('PASSENGER');
    const pendingContext = {
      shuttleTripId: TEST_SHUTTLE_ID,
      mainTripId: TEST_TRIP_ID,
      operatorId: TEST_OPERATOR_ID,
      driverUserId: TEST_USER_ID,
      allowed: true,
      scope: 'PASSENGER',
      stops: [{ status: 'PENDING' }],
    };
    const pickedUpContext = {
      ...pendingContext,
      stops: [{ status: 'PICKED_UP' }],
    };
    shuttleGetContext
      .mockResolvedValueOnce(pendingContext)
      .mockResolvedValueOnce(pickedUpContext)
      .mockResolvedValueOnce({ ...pendingContext, allowed: false, scope: null });

    const pendingSocket = await connectSocket(token);
    const pending = await emitWithAck<JoinTripTrackingAck>(pendingSocket, 'joinShuttleTracking', {
      shuttleTripId: TEST_SHUTTLE_ID,
    });
    pendingSocket.disconnect();

    const pickedUpSocket = await connectSocket(token);
    const pickedUp = await emitWithAck<JoinTripTrackingAck>(pickedUpSocket, 'joinShuttleTracking', {
      shuttleTripId: TEST_SHUTTLE_ID,
    });
    pickedUpSocket.disconnect();

    const terminalSocket = await connectSocket(token);
    const terminal = await emitWithAck<JoinTripTrackingAck>(terminalSocket, 'joinShuttleTracking', {
      shuttleTripId: TEST_SHUTTLE_ID,
    });
    terminalSocket.disconnect();

    expect(pending).toEqual(expect.objectContaining({ success: true, scope: 'PASSENGER' }));
    expect(pickedUp).toEqual(expect.objectContaining({ success: true, scope: 'PASSENGER' }));
    expect(terminal).toEqual({ success: false, error: 'ACCESS_DENIED' });
    expect(shuttleGetContext).toHaveBeenCalledTimes(3);
  });

  it('broadcasts published coordinates while off-route receives raw coordinates', async () => {
    routePeek.mockReturnValue({
      tripId: TEST_TRIP_ID,
      points: [
        { latitude: 10.7, longitude: 106.66 },
        { latitude: 10.9, longitude: 106.66 },
      ],
    });
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    await emitWithAck<JoinTripTrackingAck>(socket, 'joinTripTracking', { tripId: TEST_TRIP_ID });
    const publishedPromise = waitForEvent<GpsUpdateEvent>(socket, 'gps:update');

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());
    const published = await publishedPromise;
    await waitForCondition(() => offRouteHandleGpsUpdate.mock.calls.length === 1);
    await waitForCondition(() => etaHandleGpsUpdate.mock.calls.length === 1);

    expect(ack).toEqual({ success: true });
    expect(published.longitude).toBeCloseTo(106.66, 6);
    const rawDetection = offRouteHandleGpsUpdate.mock.calls[0]?.[0] as GpsUpdateEvent;
    const publishedDetection = etaHandleGpsUpdate.mock.calls[0]?.[0] as GpsUpdateEvent;
    expect(rawDetection.longitude).toBe(106.660172);
    expect(publishedDetection.longitude).toBeCloseTo(106.66, 6);
    socket.disconnect();
  });

  it('rejects gps:update when Redis write fails', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    redisEval.mockRejectedValueOnce(new Error('REDIS_DOWN'));

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());

    expect(ack).toEqual({ success: false, error: 'TRACKING_UNAVAILABLE' });
    expect(offRouteHandleGpsUpdate).not.toHaveBeenCalled();
    expect(etaHandleGpsUpdate).not.toHaveBeenCalled();
    expect(sharedPublishGps).not.toHaveBeenCalled();
    socket.disconnect();
  });

  it('does not append, broadcast, or detect a duplicate gps:update', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    const payload = createGpsPayload();
    redisEval.mockResolvedValueOnce(1).mockResolvedValueOnce(0);

    const first = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', payload);
    await waitForCondition(() => offRouteHandleGpsUpdate.mock.calls.length === 1);
    const duplicate = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', payload);
    await new Promise((resolve) => setTimeout(resolve, 25));

    expect(first).toEqual({ success: true });
    expect(duplicate).toEqual({ success: true });
    expect(redisEval).toHaveBeenCalledTimes(2);
    expect(offRouteHandleGpsUpdate).toHaveBeenCalledTimes(1);
    expect(etaHandleGpsUpdate).toHaveBeenCalledTimes(1);
    expect(sharedPublishGps).toHaveBeenCalledTimes(1);
    socket.disconnect();
  });

  it('rejects the same gps operation identity with a different payload', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    redisEval.mockResolvedValueOnce(-1);

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());

    expect(ack).toEqual({ success: false, error: 'IDEMPOTENCY_KEY_REUSED' });
    expect(offRouteHandleGpsUpdate).not.toHaveBeenCalled();
    expect(etaHandleGpsUpdate).not.toHaveBeenCalled();
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
    expect(sharedPublishEta).toHaveBeenCalledWith({ ...etaUpdate, delayed: false });
    expect(sharedPublishStatus).not.toHaveBeenCalled();
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
    expect(sharedPublishEta).toHaveBeenCalledWith(delayedEtaUpdate);
    expect(sharedPublishStatus).toHaveBeenCalledWith({
      tripId: TEST_TRIP_ID,
      status: 'DELAYED',
      delayMinutes: 35,
      updatedAt: etaUpdate.updatedAt,
    });
    socket.disconnect();
  });

  it('keeps the private GPS ack and detection flow successful when public broadcasting throws', async () => {
    const token = await signIdentityToken('DRIVER', TEST_OPERATOR_ID);
    const socket = await connectSocket(token);
    sharedPublishGps.mockImplementationOnce(() => {
      throw new Error('shared namespace unavailable');
    });

    const ack = await emitWithAck<GpsUpdateAck>(socket, 'gps:update', createGpsPayload());

    expect(ack).toEqual({ success: true });
    await waitForCondition(() => etaHandleGpsUpdate.mock.calls.length === 1);
    expect(offRouteHandleGpsUpdate).toHaveBeenCalledTimes(1);
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
    return connectWithAuth({ token });
  }

  function connectWithAuth(auth: Record<string, string>): Promise<Socket> {
    return new Promise((resolve, reject) => {
      const socket = io(`http://127.0.0.1:${port}`, {
        path: TRACKING_SOCKET_PATH,
        auth,
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

async function waitForCondition(condition: () => boolean): Promise<void> {
  const deadline = Date.now() + ACK_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (condition()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error('WAIT_FOR_CONDITION_TIMEOUT');
}

function createTestEnv(publicKeyPem: string): Env {
  return {
    NODE_ENV: 'test',
    PORT: 3001,
    GATEWAY_URL: 'http://gateway:3000',
    INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa',
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
    TRIP_SERVICE_BASE_URL: 'http://trip.test',
    BOOKING_SERVICE_BASE_URL: 'http://booking.test',
    PARCEL_SERVICE_BASE_URL: 'http://parcel.test',
    TRIP_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization',
    BOOKING_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/bookings',
    PARCEL_TRACKING_AUTH_PATH: '/internal/v1/trips/:tripId/tracking-authorization/parcels',
    TRACKING_AUTH_HTTP_TIMEOUT_MS: 2_000,
    TRACKING_CORS_ORIGIN: '*',
    TRACKING_SWAGGER_ENABLED: true,
    TRACKING_GPS_FLUSH_ENABLED: false,
    TRACKING_GPS_FLUSH_INTERVAL_MS: 300_000,
    TRACKING_TRIP_DELAY_ENABLED: false,
    TRACKING_TRIP_DELAY_INTERVAL_MS: 300_000,
    TRACKING_OUTBOX_PUBLISH_ENABLED: false,
    TRACKING_OUTBOX_PUBLISH_INTERVAL_MS: 5_000,
    TRACKING_OUTBOX_PUBLISH_BATCH_SIZE: 25,
    TRIP_ROUTE_STOPS_PATH: '/internal/v1/trips/:tripId/route-stops',
    TRIP_ROUTE_GEOMETRY_PATH: '/internal/v1/trips/:tripId/route-geometry',
    BOOKING_PICKUP_BOOKINGS_PATH: '/internal/v1/trips/:tripId/stops/:stopId/pickup-bookings',
    TRACKING_DATA_PROVIDER_TIMEOUT_MS: 2_000,
    TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS: 300,
    TRACKING_ROUTE_GEOMETRY_CACHE_TTL_SECONDS: 600,
    TRACKING_SHARE_TOKEN_SECRET: 'phase13-test-share-token-secret-32-bytes',
    TRACKING_SHARE_PAGE_URL: 'http://localhost:5173/trip-sharing',
    TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400,
    TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 60,
    TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 20,
    TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 60,
    GOOGLE_ROUTES_ENABLED: false,
    GOOGLE_ROUTES_API_KEY: '',
    GOOGLE_ROUTES_BASE_URL: 'https://routes.googleapis.com',
    TRACKING_GOOGLE_ROUTES_TIMEOUT_MS: 1_500,
    TRACKING_ETA_MIN_INTERVAL_SECONDS: 60,
    TRACKING_ETA_CACHE_TTL_SECONDS: 60,
    TRACKING_ETA_FAILURE_COOLDOWN_SECONDS: 300,
  };
}

function readListeningPort(app: INestApplication): number {
  const server = app.getHttpServer() as {
    address(): string | { port: number } | null;
  };
  const address = server.address();
  if (typeof address === 'object' && address !== null) {
    return address.port;
  }

  throw new Error('TRACKING_E2E_PORT_UNAVAILABLE');
}
