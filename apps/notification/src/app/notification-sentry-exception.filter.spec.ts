import { BadRequestException, InternalServerErrorException } from '@nestjs/common';
import type { ArgumentsHost } from '@nestjs/common';
import * as Sentry from '@sentry/nestjs';
import { NotificationSentryExceptionFilter } from './notification-sentry-exception.filter';

jest.mock('@sentry/nestjs', () => ({
  captureException: jest.fn(),
}));

describe('NotificationSentryExceptionFilter', () => {
  const response = {
    status: jest.fn().mockReturnThis(),
    json: jest.fn(),
  };
  const host = {
    switchToHttp: () => ({
      getResponse: () => response,
      getRequest: () => ({ url: '/test', headers: { 'x-request-id': 'request-id' } }),
    }),
  } as unknown as ArgumentsHost;

  beforeEach(() => {
    jest.clearAllMocks();
    response.status.mockReturnThis();
  });

  it('does not capture expected 4xx errors', () => {
    const filter = new NotificationSentryExceptionFilter();

    filter.catch(new BadRequestException({ errorCode: 'VALIDATION_FAILED' }), host);

    expect(Sentry.captureException).not.toHaveBeenCalled();
  });

  it('captures 5xx errors and preserves the API response envelope', () => {
    const filter = new NotificationSentryExceptionFilter();
    const exception = new InternalServerErrorException({ errorCode: 'INTERNAL_ERROR' });

    filter.catch(exception, host);

    expect(Sentry.captureException).toHaveBeenCalledWith(exception);
    expect(response.status).toHaveBeenCalledWith(500);
    expect(response.json).toHaveBeenCalledWith(
      expect.objectContaining({ success: false, statusCode: 500 }),
    );
  });
});
