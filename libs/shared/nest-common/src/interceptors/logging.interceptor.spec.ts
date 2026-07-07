import type { CallHandler, ExecutionContext } from '@nestjs/common';
import type { Logger as PinoLogger } from 'pino';
import { of } from 'rxjs';
import { LoggingInterceptor } from './logging.interceptor';

function makeContext(url: string): ExecutionContext {
  const req = { method: 'GET', originalUrl: url, url, headers: {} };
  const res = { statusCode: 200 };
  return {
    getType: () => 'http',
    switchToHttp: () => ({ getRequest: () => req, getResponse: () => res }),
  } as unknown as ExecutionContext;
}

const next: CallHandler = { handle: () => of(undefined) };

describe('LoggingInterceptor URL redaction', () => {
  it('redacts token query values but keeps other params', (done) => {
    const info = jest.fn();
    const logger = { info, warn: jest.fn(), error: jest.fn() } as unknown as PinoLogger;
    const interceptor = new LoggingInterceptor(logger);

    interceptor.intercept(makeContext('/auth/set-password?token=abc-123&lang=vi'), next).subscribe({
      complete: () => {
        expect(info).toHaveBeenCalledWith(
          expect.objectContaining({ url: '/auth/set-password?token=[REDACTED]&lang=vi' }),
          'request.start',
        );
        done();
      },
    });
  });

  it('leaves URLs without a token param untouched', (done) => {
    const info = jest.fn();
    const logger = { info, warn: jest.fn(), error: jest.fn() } as unknown as PinoLogger;
    const interceptor = new LoggingInterceptor(logger);

    interceptor.intercept(makeContext('/v1/users/me?page=2'), next).subscribe({
      complete: () => {
        expect(info).toHaveBeenCalledWith(
          expect.objectContaining({ url: '/v1/users/me?page=2' }),
          'request.start',
        );
        done();
      },
    });
  });
});
