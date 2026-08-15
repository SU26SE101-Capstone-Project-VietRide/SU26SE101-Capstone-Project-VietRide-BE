import { INestApplication } from '@nestjs/common';
import { APP_FILTER, APP_INTERCEPTOR } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { Test } from '@nestjs/testing';
import { ApiResponseExceptionFilter, ApiResponseInterceptor } from '@vietride/nest-common';
import { TRACKING_JWT_VERIFIER } from '../app/tokens';
import type { TrackingUser } from '../auth/tracking-user.types';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { OperatorFleetAuthGuard } from './operator-fleet-auth.guard';
import { OperatorFleetController } from './operator-fleet.controller';
import { OperatorFleetService } from './operator-fleet.service';

describe('OperatorFleetController (e2e)', () => {
  let app: INestApplication;
  let port: number;
  const getLatest = jest.fn(async () => ({ items: [], generatedAt: new Date().toISOString() }));

  beforeAll(async () => {
    const verifier: UserJwtVerifier = {
      verify: async (token: string): Promise<TrackingUser> => {
        if (token === 'operator-token') {
          return {
            userId: '11111111-1111-4111-8111-111111111111',
            role: 'OPERATOR_ADMIN',
            operatorId: '22222222-2222-4222-8222-222222222222',
          };
        }
        if (token === 'passenger-token') {
          return { userId: '33333333-3333-4333-8333-333333333333', role: 'PASSENGER' };
        }
        throw new Error('UNAUTHORIZED');
      },
    };
    const moduleRef = await Test.createTestingModule({
      controllers: [OperatorFleetController],
      providers: [
        OperatorFleetAuthGuard,
        { provide: OperatorFleetService, useValue: { getLatest } },
        { provide: TRACKING_JWT_VERIFIER, useValue: verifier },
        { provide: APP_FILTER, useValue: new ApiResponseExceptionFilter() },
        { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
      ],
    }).compile();
    app = moduleRef.createNestApplication();
    await app.listen(0);
    port = readListeningPort(app);
  });

  beforeEach(() => getLatest.mockClear());

  afterAll(async () => {
    if (app) await app.close();
  });

  it('opts into Shuttle aggregation for an operator', async () => {
    const response = await request('?include=shuttle&status=IN_PROGRESS', 'operator-token');

    expect(response.status).toBe(200);
    expect(getLatest).toHaveBeenCalledWith(
      '22222222-2222-4222-8222-222222222222',
      'IN_PROGRESS',
      true,
    );
  });

  it('rejects invalid include values and non-operator principals', async () => {
    const invalid = await request('?include=bus', 'operator-token');
    const passenger = await request('?include=shuttle', 'passenger-token');

    expect(invalid.status).toBe(400);
    expect(passenger.status).toBe(403);
    expect(getLatest).not.toHaveBeenCalled();
  });

  it('requires auth and documents the response matrix', async () => {
    const unauthorized = await fetch(`http://127.0.0.1:${port}/v1/tracking/operator/fleet-latest`);
    expect(unauthorized.status).toBe(401);

    const document = SwaggerModule.createDocument(app, new DocumentBuilder().build());
    const responses = document.paths['/v1/tracking/operator/fleet-latest']?.get?.responses;
    expect(Object.keys(responses ?? {})).toEqual(
      expect.arrayContaining(['200', '400', '401', '403', '503']),
    );
  });

  function request(query: string, token: string): Promise<Response> {
    return fetch(`http://127.0.0.1:${port}/v1/tracking/operator/fleet-latest${query}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  }
});

function readListeningPort(app: INestApplication): number {
  const server = app.getHttpServer() as {
    address(): string | { port: number } | null;
  };
  const address = server.address();
  if (typeof address === 'object' && address !== null) return address.port;
  throw new Error('TRACKING_OPERATOR_FLEET_E2E_PORT_UNAVAILABLE');
}
