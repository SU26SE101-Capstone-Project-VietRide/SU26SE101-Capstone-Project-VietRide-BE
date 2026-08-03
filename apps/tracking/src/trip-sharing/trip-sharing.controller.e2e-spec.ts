import {
  ForbiddenException,
  INestApplication,
  ServiceUnavailableException,
} from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { exportSPKI, generateKeyPair, SignJWT, type KeyLike } from 'jose';
import { ENV_TOKEN, TRACKING_JWT_VERIFIER } from '../app/tokens';
import { JoseUserJwtVerifier } from '../auth/user-jwt.verifier';
import type { Env } from '../config/env.schema';
import { BookingOwnerAuthorizationProvider } from './booking-owner-authorization.provider';
import { TripShareGrantRepository } from './trip-share-grant.repository';
import { TripShareGrantService } from './trip-share-grant.service';
import { TripShareIdempotencyService } from './trip-share-idempotency.service';
import { TripShareOwnerController } from './trip-share-owner.controller';
import { TripShareOwnerJwtGuard } from './trip-share-owner-jwt.guard';
import { TripShareOwnerService } from './trip-share-owner.service';
import { TripShareTokenCodec } from './trip-share-token.codec';
import { TripShareTripSnapshotProvider } from './trip-share-trip-snapshot.provider';
import { TripShareRealtimePublisher } from './trip-share-realtime.publisher';

const TRIP_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const GRANT_ID = '33333333-3333-4333-8333-333333333333';
const IDEMPOTENCY_KEY = '44444444-4444-4444-8444-444444444444';
const EXPIRES_AT = new Date('2026-08-04T09:30:00.000Z');
const ISSUER = 'vietride-identity';
const AUDIENCE = 'vietride-api';

interface Envelope<T> {
  success: boolean;
  statusCode: number;
  data?: T;
  error?: { code: string; message: string };
}

describe('TripShareOwnerController (e2e)', () => {
  let app: INestApplication;
  let port: number;
  let privateKey: KeyLike;
  const booking = { requireBookingOwner: jest.fn() };
  const trips = { getTrip: jest.fn() };
  const grants = { ensureActive: jest.fn() };
  const repository = {
    findActiveByOwnerTrip: jest.fn(),
    revokeGrantById: jest.fn(),
    revokeOwnActiveGrant: jest.fn(),
    revokeOwnActiveGrantById: jest.fn(),
  };
  const realtime = { revokeGrant: jest.fn() };
  const idempotency = { begin: jest.fn(), complete: jest.fn(), abandon: jest.fn() };

  beforeAll(async () => {
    const keys = await generateKeyPair('RS256');
    privateKey = keys.privateKey;
    const publicKeyPem = await exportSPKI(keys.publicKey);
    const env = createTestEnv(publicKeyPem);
    const moduleRef = await Test.createTestingModule({
      controllers: [TripShareOwnerController],
      providers: [
        TripShareOwnerService,
        TripShareOwnerJwtGuard,
        TripShareTokenCodec,
        { provide: ENV_TOKEN, useValue: env },
        { provide: TRACKING_JWT_VERIFIER, useClass: JoseUserJwtVerifier },
        { provide: BookingOwnerAuthorizationProvider, useValue: booking },
        { provide: TripShareTripSnapshotProvider, useValue: trips },
        { provide: TripShareGrantService, useValue: grants },
        { provide: TripShareGrantRepository, useValue: repository },
        { provide: TripShareIdempotencyService, useValue: idempotency },
        { provide: TripShareRealtimePublisher, useValue: realtime },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();
    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  beforeEach(() => {
    jest.clearAllMocks();
    booking.requireBookingOwner.mockResolvedValue(undefined);
    trips.getTrip.mockResolvedValue({ tripId: TRIP_ID, status: 'IN_PROGRESS' });
    grants.ensureActive.mockResolvedValue({
      grant: { id: GRANT_ID, expiresAt: EXPIRES_AT },
      token: 'unused-by-owner-service',
    });
    repository.revokeGrantById.mockResolvedValue(1);
    repository.revokeOwnActiveGrant.mockResolvedValue(1);
    repository.revokeOwnActiveGrantById.mockResolvedValue(true);
    repository.findActiveByOwnerTrip.mockResolvedValue({ id: GRANT_ID });
    realtime.revokeGrant.mockResolvedValue(undefined);
    idempotency.begin.mockResolvedValue({ state: 'acquired', ownerToken: 'owner-lock' });
    idempotency.complete.mockResolvedValue(undefined);
    idempotency.abandon.mockResolvedValue(undefined);
  });

  afterAll(async () => {
    if (app) await app.close();
  });

  it('returns 401 envelopes for missing and invalid Identity tokens', async () => {
    const missing = await request('PUT', TRIP_ID, undefined, IDEMPOTENCY_KEY);
    const invalid = await request('PUT', TRIP_ID, 'invalid', IDEMPOTENCY_KEY);
    expect([missing.status, invalid.status]).toEqual([401, 401]);
    expect([missing.body.error?.code, invalid.body.error?.code]).toEqual(['UNAUTHORIZED', 'UNAUTHORIZED']);
  });

  it('returns 403 before downstream calls for a non-passenger token', async () => {
    const response = await request('PUT', TRIP_ID, await signToken('DRIVER'), IDEMPOTENCY_KEY);
    expect(response.status).toBe(403);
    expect(response.body.error?.code).toBe('ACCESS_DENIED');
    expect(booking.requireBookingOwner).not.toHaveBeenCalled();
  });

  it('returns 400 for an invalid trip id and 422 for missing or malformed idempotency keys', async () => {
    const token = await signToken('PASSENGER');
    expect((await request('PUT', 'not-a-uuid', token, IDEMPOTENCY_KEY)).status).toBe(400);
    expect((await request('PUT', TRIP_ID, token)).status).toBe(422);
    expect((await request('PUT', TRIP_ID, token, 'not-a-uuid')).status).toBe(422);
  });

  it('returns the ApiResponse envelope with stable fragment token and expiry', async () => {
    const response = await request<{ shareUrl: string; expiresAt: string }>(
      'PUT', TRIP_ID, await signToken('PASSENGER'), IDEMPOTENCY_KEY,
    );
    expect(response.status).toBe(200);
    expect(response.body.success).toBe(true);
    const data = response.body.data;
    expect(data).toBeDefined();
    if (!data) throw new Error('Expected trip share response data');
    expect(data.expiresAt).toBe(EXPIRES_AT.toISOString());
    expect(new URL(data.shareUrl).hash).toMatch(/^#token=v1\./);
    expect(booking.requireBookingOwner).toHaveBeenCalledWith(USER_ID, TRIP_ID);
    expect(trips.getTrip).toHaveBeenCalledTimes(2);
  });

  it('maps parcel-only/booking denial to 403 without disclosing Trip status', async () => {
    booking.requireBookingOwner.mockRejectedValueOnce(new ForbiddenException({ errorCode: 'ACCESS_DENIED', detail: 'Denied' }));
    const response = await request('PUT', TRIP_ID, await signToken('PASSENGER'), IDEMPOTENCY_KEY);
    expect(response.status).toBe(403);
    expect(response.body.error?.code).toBe('ACCESS_DENIED');
    expect(trips.getTrip).not.toHaveBeenCalled();
  });

  it('returns 409 for a non-active Trip', async () => {
    trips.getTrip.mockResolvedValueOnce({ tripId: TRIP_ID, status: 'COMPLETED' });
    const response = await request('PUT', TRIP_ID, await signToken('PASSENGER'), IDEMPOTENCY_KEY);
    expect(response.status).toBe(409);
    expect(response.body.error?.code).toBe('TRACKING_TRIP_NOT_ACTIVE');
  });

  it.each([
    ['booking', new ServiceUnavailableException({ errorCode: 'TRACKING_AUTH_UNAVAILABLE' })],
    ['trip', new ServiceUnavailableException({ errorCode: 'TRACKING_TRIP_UNAVAILABLE' })],
  ])('returns 503 when the %s downstream is unavailable', async (target, error) => {
    if (target === 'booking') booking.requireBookingOwner.mockRejectedValueOnce(error);
    else trips.getTrip.mockRejectedValueOnce(error);
    const response = await request('PUT', TRIP_ID, await signToken('PASSENGER'), IDEMPOTENCY_KEY);
    expect(response.status).toBe(503);
    expect(idempotency.abandon).toHaveBeenCalledWith('owner-lock');
  });

  it('DELETE revokes only the authenticated owner and replays a stable 200', async () => {
    const token = await signToken('PASSENGER');
    const first = await request<{ revoked: true }>('DELETE', TRIP_ID, token, IDEMPOTENCY_KEY);
    idempotency.begin.mockResolvedValueOnce({ state: 'replay', outcome: { kind: 'REVOKED', revoked: true } });
    const replay = await request<{ revoked: true }>('DELETE', TRIP_ID, token, IDEMPOTENCY_KEY);
    expect(first.status).toBe(200);
    expect(replay.body.data).toEqual({ revoked: true });
    expect(repository.revokeOwnActiveGrantById).toHaveBeenCalledTimes(1);
    expect(repository.revokeOwnActiveGrantById).toHaveBeenCalledWith(
      GRANT_ID,
      TRIP_ID,
      USER_ID,
      expect.any(Date),
    );
    expect(repository.revokeOwnActiveGrant).not.toHaveBeenCalled();
    expect(booking.requireBookingOwner).not.toHaveBeenCalled();
    expect(trips.getTrip).not.toHaveBeenCalled();
  });

  it('documents the complete owner endpoint response sets', () => {
    const document = SwaggerModule.createDocument(app, new DocumentBuilder().build());
    const path = document.paths['/v1/tracking/trips/{tripId}/share-link'];
    expect(Object.keys(path?.put?.responses ?? {})).toEqual(
      expect.arrayContaining(['200', '400', '401', '403', '404', '409', '422', '429', '503']),
    );
    expect(Object.keys(path?.delete?.responses ?? {})).toEqual(
      expect.arrayContaining(['200', '400', '401', '403', '409', '422', '429', '503']),
    );

    const schemas = document.components?.schemas ?? {};
    for (const schemaName of [
      'TripShareLinkEnvelopeSwaggerDto',
      'TripShareRevokedEnvelopeSwaggerDto',
      'TripShareErrorEnvelopeSwaggerDto',
    ]) {
      expect(schemas[schemaName]).toEqual(expect.objectContaining({
        required: expect.arrayContaining(['meta']),
        properties: expect.objectContaining({
          meta: { $ref: '#/components/schemas/TripShareEnvelopeMetaSwaggerDto' },
        }),
      }));
    }
    expect(schemas['TripShareEnvelopeMetaSwaggerDto']).toEqual(expect.objectContaining({
      required: expect.arrayContaining(['traceId', 'timestamp']),
      properties: expect.objectContaining({
        traceId: expect.objectContaining({ type: 'string' }),
        timestamp: expect.objectContaining({ type: 'string', format: 'date-time' }),
      }),
    }));
  });

  async function signToken(role: string): Promise<string> {
    return new SignJWT({ role, email: 'phase13-owner@vietride.local' })
      .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: 'phase13-owner-key' })
      .setSubject(USER_ID)
      .setIssuer(ISSUER)
      .setAudience(AUDIENCE)
      .setIssuedAt()
      .setExpirationTime('15m')
      .sign(privateKey);
  }

  async function request<T = unknown>(
    method: 'PUT' | 'DELETE',
    tripId: string,
    token?: string,
    idempotencyKey?: string,
  ): Promise<{ status: number; body: Envelope<T> }> {
    const headers: Record<string, string> = {};
    if (token) headers.Authorization = `Bearer ${token}`;
    if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;
    const response = await fetch(`http://127.0.0.1:${port}/v1/tracking/trips/${tripId}/share-link`, {
      method,
      headers,
    });
    return { status: response.status, body: await response.json() as Envelope<T> };
  }
});

function createTestEnv(publicKeyPem: string): Env {
  return {
    JWT_PUBLIC_KEY_URL: 'http://identity.test/v1/.well-known/jwks.json',
    USER_JWT_PUBLIC_KEY: publicKeyPem,
    JWT_ISSUER: ISSUER,
    JWT_AUDIENCE: AUDIENCE,
    TRACKING_SHARE_TOKEN_SECRET: 'phase13-owner-controller-secret-at-least-32-bytes',
    TRACKING_SHARE_PAGE_URL: 'https://app.vietride.vn/trip-sharing',
  } as Env;
}

function readListeningPort(app: INestApplication): number {
  const address = (app.getHttpServer() as { address(): string | { port: number } | null }).address();
  if (typeof address === 'object' && address !== null) return address.port;
  throw new Error('TRACKING_SHARE_OWNER_E2E_PORT_UNAVAILABLE');
}
