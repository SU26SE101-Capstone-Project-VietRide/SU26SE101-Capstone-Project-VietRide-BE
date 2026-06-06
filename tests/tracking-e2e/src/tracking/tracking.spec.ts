import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { RedisService } from '@vietride/nest-redis';
import { io, type Socket } from 'socket.io-client';
/* eslint-disable @nx/enforce-module-boundaries */
import {
  TRACKING_AUTHORIZATION_ADAPTER,
  TRACKING_JWT_VERIFIER,
} from '../../../../apps/tracking/src/app/tokens';
import type { UserJwtVerifier } from '../../../../apps/tracking/src/auth/user-jwt.verifier';
import type { TrackingAuthorizationAdapter } from '../../../../apps/tracking/src/authorization/tracking-authorization.adapter';
import {
  TRACKING_SOCKET_PATH,
  trackingGpsBufferKey,
  trackingLatestKey,
} from '../../../../apps/tracking/src/location/location.constants';
import { LocationGateway } from '../../../../apps/tracking/src/location/location.gateway';
import { LocationService } from '../../../../apps/tracking/src/location/location.service';
/* eslint-enable @nx/enforce-module-boundaries */

const TRIP_ID = '11111111-1111-4111-8111-111111111111';

describe('Tracking Socket.IO MVP (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let redisClient: {
    multi: jest.Mock;
  };
  const sockets: Socket[] = [];

  beforeAll(async () => {
    redisClient = createRedisClient();

    const moduleRef = await Test.createTestingModule({
      providers: [
        LocationGateway,
        LocationService,
        { provide: RedisService, useValue: { getClient: () => redisClient } },
        { provide: TRACKING_JWT_VERIFIER, useValue: createJwtVerifier() },
        { provide: TRACKING_AUTHORIZATION_ADAPTER, useValue: createAuthorizationAdapter() },
      ],
    })
      .compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    const address = app.getHttpServer().address();
    port = typeof address === 'string' ? Number(address) : address.port;
  });

  afterEach(() => {
    for (const socket of sockets.splice(0)) {
      socket.disconnect();
    }
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('rejects a connection without token', async () => {
    await expect(connectSocket()).rejects.toThrow('UNAUTHORIZED');
  });

  it('returns VALIDATION_ERROR for invalid joinTripTracking payload', async () => {
    const passenger = await connectSocket('passenger-token');
    const ack = await emitWithAck(passenger, 'joinTripTracking', { tripId: 'not-a-uuid' });

    expect(ack).toEqual({
      success: false,
      error: 'VALIDATION_ERROR',
      message: 'Invalid tripId',
    });
  });

  it('joins an authorized trip room', async () => {
    const passenger = await connectSocket('passenger-token');
    const ack = await emitWithAck(passenger, 'joinTripTracking', { tripId: TRIP_ID });

    expect(ack).toEqual({
      success: true,
      tripId: TRIP_ID,
      room: `trip:${TRIP_ID}`,
      scope: 'BOOKING_OWNER',
    });
  });

  it('broadcasts gps:update to clients in the trip room', async () => {
    const passenger = await connectSocket('passenger-token');
    const driver = await connectSocket('driver-token');
    await emitWithAck(passenger, 'joinTripTracking', { tripId: TRIP_ID });

    const received = once(passenger, 'gps:update');
    const payload = {
      tripId: TRIP_ID,
      latitude: 10.7769,
      longitude: 106.7009,
      speedKmh: 42,
      headingDeg: 90,
      recordedAt: '2026-05-31T12:00:00.000Z',
    };
    const ack = await emitWithAck(driver, 'gps:update', payload);

    await expect(received).resolves.toMatchObject(payload);
    expect(ack).toEqual({ success: true });
    expect(redisClient.multi).toHaveBeenCalledWith();
  });

  async function connectSocket(token?: string): Promise<Socket> {
    const socket = io(`http://127.0.0.1:${port}`, {
      path: TRACKING_SOCKET_PATH,
      auth: token ? { token } : {},
      transports: ['websocket'],
      forceNew: true,
      reconnection: false,
    });
    sockets.push(socket);

    await new Promise<void>((resolve, reject) => {
      socket.once('connect', resolve);
      socket.once('connect_error', (error) => reject(error));
    });

    return socket;
  }
});

function createJwtVerifier(): UserJwtVerifier {
  return {
    async verify(token: string) {
      if (token === 'driver-token') return { userId: 'driver-1', role: 'DRIVER' };
      if (token === 'passenger-token') return { userId: 'passenger-1', role: 'PASSENGER' };
      throw new Error('UNAUTHORIZED');
    },
  };
}

function createAuthorizationAdapter(): TrackingAuthorizationAdapter {
  return {
    async authorizeTripTracking(user) {
      if (user.role === 'PASSENGER') return { allowed: true, scope: 'BOOKING_OWNER' };
      if (user.role === 'DRIVER') return { allowed: true, scope: 'DRIVER' };
      return { allowed: false, error: 'ACCESS_DENIED' };
    },
  };
}

function createRedisClient() {
  return {
    multi: jest.fn(() => {
      interface RedisMultiChain {
        set: jest.Mock<RedisMultiChain, [string, string, string, number]>;
        rpush: jest.Mock<RedisMultiChain, [string, string]>;
        sadd: jest.Mock<RedisMultiChain, [string, string]>;
        exec: jest.Mock<Promise<never[]>, []>;
      }

      const chain: RedisMultiChain = {
        /* eslint-disable @typescript-eslint/no-unused-vars */
        set: jest.fn((key: string, _value: string, _mode: string, _ttl: number): RedisMultiChain => {
          expect(key).toBe(trackingLatestKey(TRIP_ID));
          return chain;
        }),
        rpush: jest.fn((key: string, _value: string): RedisMultiChain => {
          expect(key).toBe(trackingGpsBufferKey(TRIP_ID));
          return chain;
        }),
        sadd: jest.fn((_key: string, _value: string): RedisMultiChain => chain),
        /* eslint-enable @typescript-eslint/no-unused-vars */
        exec: jest.fn(async () => []),
      };
      return chain;
    }),
  };
}

function emitWithAck<TPayload>(socket: Socket, event: string, payload: TPayload): Promise<unknown> {
  return new Promise((resolve) => {
    socket.emit(event, payload, resolve);
  });
}

function once(socket: Socket, event: string): Promise<unknown> {
  return new Promise((resolve) => {
    socket.once(event, resolve);
  });
}
