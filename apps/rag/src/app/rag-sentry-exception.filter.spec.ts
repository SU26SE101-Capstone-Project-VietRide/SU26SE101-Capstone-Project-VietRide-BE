import { BadRequestException, InternalServerErrorException } from '@nestjs/common';
import type { ArgumentsHost } from '@nestjs/common';
import * as Sentry from '@sentry/nestjs';
import { RagSentryExceptionFilter } from './rag-sentry-exception.filter';

jest.mock('@sentry/nestjs', () => ({
  captureException: jest.fn(),
}));

describe('RagSentryExceptionFilter', () => {
  let filter: RagSentryExceptionFilter;
  let host: ArgumentsHost;

  beforeEach(() => {
    jest.clearAllMocks();
    filter = new RagSentryExceptionFilter();
    host = {
      switchToHttp: () => ({
        getResponse: () => ({
          status: jest.fn().mockReturnThis(),
          json: jest.fn(),
        }),
        getRequest: () => ({ url: '/test', headers: { 'x-request-id': 'test-req' } }),
      }),
    } as unknown as ArgumentsHost;
  });

  it('does not call Sentry.captureException for 4xx errors', () => {
    const exception = new BadRequestException({ errorCode: 'VALIDATION_FAILED', detail: 'Bad' });

    filter.catch(exception, host);

    expect(Sentry.captureException).not.toHaveBeenCalled();
  });

  it('calls Sentry.captureException for 5xx errors', () => {
    const exception = new InternalServerErrorException({ errorCode: 'INTERNAL_ERROR', detail: 'Oops' });

    filter.catch(exception, host);

    expect(Sentry.captureException).toHaveBeenCalledWith(exception);
  });

  it('calls Sentry.captureException for non-HttpException 5xx errors', () => {
    const exception = new Error('Something crashed');

    filter.catch(exception, host);

    expect(Sentry.captureException).toHaveBeenCalledWith(exception);
  });

  it('stills sends ApiResponse envelope after Sentry capture for 5xx', () => {
    const jsonSpy = jest.fn();
    const statusSpy = jest.fn().mockReturnValue({ json: jsonSpy });
    const localHost = {
      switchToHttp: () => ({
        getResponse: () => ({ status: statusSpy }),
        getRequest: () => ({ url: '/test', headers: { 'x-request-id': 'test-req' } }),
      }),
    } as unknown as ArgumentsHost;

    const exception = new InternalServerErrorException({ errorCode: 'INTERNAL_ERROR', detail: 'Oops' });
    filter.catch(exception, localHost);

    expect(Sentry.captureException).toHaveBeenCalledWith(exception);
    expect(statusSpy).toHaveBeenCalledWith(500);
    const jsonArg = jsonSpy.mock.calls[0][0] as Record<string, unknown>;
    expect(jsonArg).toMatchObject({ success: false, statusCode: 500 });
  });
});
