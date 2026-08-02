import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { ShuttleTrackingAuthGuard } from './shuttle-tracking-auth.guard';
import { ShuttleTrackingController } from './shuttle-tracking.controller';
import { ShuttleService, type ShuttleTrackingContext } from './shuttle.service';

const SHUTTLE_ID = '36000000-0000-4000-8000-000000000001';
const MAIN_TRIP_ID = '11111111-1111-4111-8111-111111111111';

interface ApiEnvelope<T> {
  success: boolean;
  statusCode: number;
  data?: T;
  error?: { code: string; message: string };
}

describe('ShuttleTrackingController passenger context (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let getContext: jest.MockedFunction<ShuttleService['getContext']>;
  let getPassengerContext: jest.MockedFunction<ShuttleService['getPassengerContext']>;
  let getLatest: jest.MockedFunction<ShuttleService['getLatest']>;
  let getEta: jest.MockedFunction<ShuttleService['getEta']>;

  beforeAll(async () => {
    getContext = jest.fn(async (user: TrackingUser, shuttleTripId: string) => {
      void user;
      void shuttleTripId;
      return createContext();
    });
    getPassengerContext = jest.fn(async (context: ShuttleTrackingContext) => {
      void context;
      return {
        shuttleTripId: SHUTTLE_ID,
        mainTripId: MAIN_TRIP_ID,
        ownPickups: [{
          bookingId: '44444444-4444-4444-8444-444444444444',
          pickupOrder: 3,
          latitude: 10.7,
          longitude: 106.7,
          status: 'PENDING' as const,
          stopsBeforePickup: 2,
        }],
        station: null,
      };
    });
    getLatest = jest.fn(async (shuttleTripId: string) => {
      void shuttleTripId;
      return { shuttleTripId: SHUTTLE_ID };
    });
    getEta = jest.fn(async (shuttleTripId: string) => {
      void shuttleTripId;
      return { nextPickupOrder: 3 };
    });
    const jwtVerifier: UserJwtVerifier = {
      verify: async (token: string): Promise<TrackingUser> => {
        if (token === 'passenger-token') {
          return { userId: '22222222-2222-4222-8222-222222222222', role: 'PASSENGER' };
        }
        if (token === 'driver-token') {
          return { userId: '33333333-3333-4333-8333-333333333333', role: 'DRIVER' };
        }
        throw new Error('UNAUTHORIZED');
      },
    };
    const moduleRef = await Test.createTestingModule({
      controllers: [ShuttleTrackingController],
      providers: [
        ShuttleTrackingAuthGuard,
        {
          provide: ShuttleService,
          useValue: { getContext, getPassengerContext, getLatest, getEta },
        },
        { provide: TRACKING_JWT_VERIFIER, useValue: jwtVerifier },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  beforeEach(() => {
    getContext.mockReset();
    getContext.mockResolvedValue(createContext());
    getPassengerContext.mockClear();
    getLatest.mockClear();
    getEta.mockClear();
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('reuses the guard-fetched context and returns a private no-store passenger payload', async () => {
    const fetchedContext = createContext();
    getContext.mockResolvedValueOnce(fetchedContext);
    const response = await request('/passenger-context', 'passenger-token');
    const body = (await response.json()) as ApiEnvelope<{ ownPickups: unknown[] }>;

    expect(response.status).toBe(200);
    expect(response.headers.get('cache-control')).toBe('private, no-store');
    expect(body.data?.ownPickups).toHaveLength(1);
    expect(getContext).toHaveBeenCalledTimes(1);
    expect(getPassengerContext).toHaveBeenCalledTimes(1);
    expect(getPassengerContext).toHaveBeenCalledWith(fetchedContext);
  });

  it('allows a PICKED_UP passenger to keep using latest and eta', async () => {
    getContext.mockResolvedValue(createContext('PICKED_UP'));

    const latest = await request('/latest', 'passenger-token');
    const eta = await request('/eta', 'passenger-token');

    expect(latest.status).toBe(200);
    expect(eta.status).toBe(200);
    expect(getLatest).toHaveBeenCalledTimes(1);
    expect(getEta).toHaveBeenCalledTimes(1);
  });

  it('denies terminal-only passengers and non-passenger roles', async () => {
    getContext.mockResolvedValueOnce(createContext('DELIVERED', false));
    const terminal = await request('/passenger-context', 'passenger-token');
    const terminalBody = (await terminal.json()) as ApiEnvelope<unknown>;

    expect(terminal.status).toBe(403);
    expect(terminalBody.error?.code).toBe('TRACKING_ACCESS_DENIED');
    expect(getPassengerContext).not.toHaveBeenCalled();

    const driver = await request('/passenger-context', 'driver-token');
    expect(driver.status).toBe(403);
    expect(getPassengerContext).not.toHaveBeenCalled();
  });

  it('fails closed with TRACKING_CONTEXT_UNAVAILABLE for malformed internal context', async () => {
    getContext.mockRejectedValueOnce(new Error('TRACKING_CONTEXT_UNAVAILABLE'));

    const response = await request('/passenger-context', 'passenger-token');
    const body = (await response.json()) as ApiEnvelope<unknown>;

    expect(response.status).toBe(503);
    expect(body.error?.code).toBe('TRACKING_CONTEXT_UNAVAILABLE');
  });

  it('requires authentication and documents the complete endpoint response matrix', async () => {
    const unauthorized = await fetch(
      `http://127.0.0.1:${port}/v1/tracking/shuttle-trips/${SHUTTLE_ID}/passenger-context`,
    );
    expect(unauthorized.status).toBe(401);

    const document = SwaggerModule.createDocument(app, new DocumentBuilder().build());
    const responses = document.paths[
      '/v1/tracking/shuttle-trips/{shuttleTripId}/passenger-context'
    ]?.get?.responses;
    expect(Object.keys(responses ?? {})).toEqual(
      expect.arrayContaining(['200', '400', '401', '403', '404', '503']),
    );
  });

  function request(path: string, token: string): Promise<Response> {
    return fetch(`http://127.0.0.1:${port}/v1/tracking/shuttle-trips/${SHUTTLE_ID}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  }
});

function createContext(status = 'PENDING', allowed = true): ShuttleTrackingContext {
  return {
    shuttleTripId: SHUTTLE_ID,
    mainTripId: MAIN_TRIP_ID,
    operatorId: '55555555-5555-4555-8555-555555555555',
    driverUserId: '33333333-3333-4333-8333-333333333333',
    allowed,
    scope: allowed ? 'PASSENGER' : null,
    stops: [{
      pickupOrder: 3,
      bookingId: '44444444-4444-4444-8444-444444444444',
      latitude: 10.7,
      longitude: 106.7,
      status,
      isStation: false,
      isOwnPickup: true,
    }],
    station: {
      stationId: '66666666-6666-4666-8666-666666666666',
      name: 'Station',
      latitude: 10.8,
      longitude: 106.8,
      pickupOrder: 4,
    },
  };
}

function readListeningPort(app: INestApplication): number {
  const server = app.getHttpServer() as {
    address(): string | { port: number } | null;
  };
  const address = server.address();
  if (typeof address === 'object' && address !== null) return address.port;
  throw new Error('TRACKING_SHUTTLE_REST_E2E_PORT_UNAVAILABLE');
}
