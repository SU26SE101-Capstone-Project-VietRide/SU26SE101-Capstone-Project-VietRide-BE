import { HttpException, type ArgumentsHost } from '@nestjs/common';
import type { Request, Response } from 'express';
import { ApiResponseExceptionFilter } from './api-response-exception.filter';

describe('ApiResponseExceptionFilter timestamp presentation', () => {
  const filter = new ApiResponseExceptionFilter();

  it('uses Asia/Ho_Chi_Minh +07:00 for public errors', () => {
    const fixture = host('/v1/trips/search');

    filter.catch(new HttpException({ message: 'Invalid request' }, 422), fixture.host);

    expect(fixture.json).toHaveBeenCalledWith(
      expect.objectContaining({
        statusCode: 422,
        meta: expect.objectContaining({ timestamp: expect.stringMatching(/\+07:00$/) }),
      }),
    );
  });

  it('keeps UTC Z for internal errors', () => {
    const fixture = host('/internal/v1/trips/1');

    filter.catch(new HttpException({ message: 'Unauthorized' }, 401), fixture.host);

    expect(fixture.json).toHaveBeenCalledWith(
      expect.objectContaining({
        statusCode: 401,
        meta: expect.objectContaining({ timestamp: expect.stringMatching(/Z$/) }),
      }),
    );
  });
});

function host(path: string): {
  host: ArgumentsHost;
  json: jest.Mock;
} {
  const json = jest.fn();
  const response = {
    headersSent: false,
    status: jest.fn().mockReturnValue({ json }),
  } as unknown as Response;
  const request = {
    path,
    method: 'GET',
    originalUrl: path,
    url: path,
    headers: { 'x-request-id': 'trace-1' },
  } as unknown as Request;
  const argumentsHost = {
    switchToHttp: () => ({
      getRequest: () => request,
      getResponse: () => response,
    }),
  } as unknown as ArgumentsHost;

  return { host: argumentsHost, json };
}
