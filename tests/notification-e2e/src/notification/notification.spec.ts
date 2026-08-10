import { type INestApplication } from '@nestjs/common';
import { APP_INTERCEPTOR } from '@nestjs/core';
import { Test } from '@nestjs/testing';
import { ApiResponseInterceptor } from '@vietride/nest-common';
/* eslint-disable @nx/enforce-module-boundaries */
import { HealthController } from '../../../../apps/notification/src/app/health.controller';
/* eslint-enable @nx/enforce-module-boundaries */

describe('GET /health', () => {
  let app: INestApplication;
  let baseUrl: string;

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({
      controllers: [HealthController],
      providers: [{ provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() }],
    }).compile();

    app = moduleRef.createNestApplication();
    await app.listen(0);
    const address = app.getHttpServer().address();
    const port = typeof address === 'string' ? Number(address) : address.port;
    baseUrl = `http://127.0.0.1:${port}`;
  });

  afterAll(async () => {
    await app.close();
  });

  it('returns the Notification liveness envelope', async () => {
    const res = await fetch(`${baseUrl}/health`);
    const body = await res.json() as Record<string, unknown>;

    expect(res.status).toBe(200);
    expect(body).toMatchObject({
      success: true,
      statusCode: 200,
      data: { status: 'ok', service: 'notification' },
      meta: { timestamp: expect.stringMatching(/\+07:00$/) },
    });
  });
});
