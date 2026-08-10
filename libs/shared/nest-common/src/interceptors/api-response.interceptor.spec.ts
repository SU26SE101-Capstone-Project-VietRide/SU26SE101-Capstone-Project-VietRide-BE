import type { CallHandler, ExecutionContext } from '@nestjs/common';
import { firstValueFrom, of } from 'rxjs';
import { ApiResponseInterceptor } from './api-response.interceptor';

describe('ApiResponseInterceptor', () => {
  const interceptor = new ApiResponseInterceptor();

  it('leaves successful internal payloads raw', async () => {
    const rows = [
      {
        id: '11111111-1111-4111-8111-111111111111',
        occurredAt: '2026-08-10T05:00:00Z',
      },
    ];

    const result = await firstValueFrom(
      interceptor.intercept(httpContext('/internal/v1/outbox/dlq'), handler(rows)),
    );

    expect(result).toEqual(rows);
  });

  it('wraps public successful payloads in the ADR 0004 envelope', async () => {
    const result = await firstValueFrom(
      interceptor.intercept(httpContext('/v1/tracking/trips/1/latest'), handler({ value: 1 })),
    );

    expect(result).toEqual(
      expect.objectContaining({
        success: true,
        statusCode: 200,
        data: { value: 1 },
        meta: expect.objectContaining({ timestamp: expect.stringMatching(/\+07:00$/) }),
      }),
    );
  });

  it('converts timestamps in already-wrapped public payloads', async () => {
    const body = {
      success: true,
      statusCode: 200,
      data: { departureDateTime: '2026-08-10T05:00:00Z' },
      meta: { traceId: 'trace-1', timestamp: '2026-08-10T05:00:00Z' },
    };

    const result = await firstValueFrom(
      interceptor.intercept(httpContext('/v1/trips/search'), handler(body)),
    );

    expect(result).toEqual({
      success: true,
      statusCode: 200,
      data: { departureDateTime: '2026-08-10T12:00:00.000+07:00' },
      meta: { traceId: 'trace-1', timestamp: '2026-08-10T12:00:00.000+07:00' },
    });
  });
});

function handler(body: unknown): CallHandler {
  return { handle: () => of(body) };
}

function httpContext(path: string): ExecutionContext {
  return {
    getType: () => 'http',
    switchToHttp: () => ({
      getRequest: () => ({ path, headers: {} }),
      getResponse: () => ({ statusCode: 200 }),
    }),
  } as unknown as ExecutionContext;
}
