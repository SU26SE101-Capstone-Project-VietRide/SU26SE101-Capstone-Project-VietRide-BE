import { Catch, HttpException, HttpStatus } from '@nestjs/common';
import type { ArgumentsHost, ExceptionFilter } from '@nestjs/common';
import * as Sentry from '@sentry/nestjs';
import type { Request, Response } from 'express';
import { toUtcIso, toVietnamIso } from '@vietride/nest-common';
import { createNotificationLogger } from '../notifications/notification-logger';

@Catch()
export class NotificationSentryExceptionFilter implements ExceptionFilter {
  private readonly logger = createNotificationLogger(NotificationSentryExceptionFilter.name);

  catch(exception: unknown, host: ArgumentsHost): void {
    const ctx = host.switchToHttp();
    const response = ctx.getResponse<Response>();
    const request = ctx.getRequest<Request>();
    const status =
      exception instanceof HttpException ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;
    const raw = exception instanceof HttpException ? exception.getResponse() : undefined;
    const errorCode = extractErrorCode(raw, status);
    const message = status >= 500 ? 'Internal server error' : extractMessage(raw, exception);
    const fields = extractFields(raw);
    const traceId =
      (request.headers['x-request-id'] as string | undefined) ??
      (request as { requestId?: string }).requestId;
    const path = request.path ?? (request.originalUrl ?? request.url ?? '').split('?')[0];
    const internal = path === '/internal' || path.startsWith('/internal/');

    if (status >= 500) {
      Sentry.captureException(exception);
      this.logger.error(
        {
          method: request.method,
          url: request.originalUrl ?? request.url,
          status,
          errorCode,
          traceId,
        },
        'Unhandled notification HTTP exception',
      );
    } else {
      this.logger.warn(
        {
          method: request.method,
          url: request.originalUrl ?? request.url,
          status,
          errorCode,
          traceId,
        },
        'Rejected notification HTTP request',
      );
    }

    if (!response.headersSent) {
      response.status(status).json({
        success: false,
        statusCode: status,
        error: {
          code: errorCode,
          message,
          ...(fields ? { fields } : {}),
        },
        meta: { traceId, timestamp: internal ? toUtcIso(new Date()) : toVietnamIso(new Date()) },
      });
    }
  }
}

function extractErrorCode(raw: unknown, status: number): string {
  if (raw && typeof raw === 'object') {
    const errorCode = (raw as Record<string, unknown>)['errorCode'];
    if (typeof errorCode === 'string') return errorCode;
  }
  return defaultErrorCodeForStatus(status);
}

function extractMessage(raw: unknown, exception: unknown): string {
  if (raw && typeof raw === 'object') {
    const value = raw as Record<string, unknown>;
    if (typeof value['message'] === 'string') return value['message'];
    if (Array.isArray(value['message'])) return value['message'].join('; ');
    if (typeof value['detail'] === 'string') return value['detail'];
  }
  if (typeof raw === 'string') return raw;
  if (exception instanceof Error) return exception.message;
  return 'Unexpected error';
}

function extractFields(raw: unknown): Array<{ field: string; message: string }> | undefined {
  if (!raw || typeof raw !== 'object') return undefined;
  const errors = (raw as Record<string, unknown>)['errors'];
  if (!Array.isArray(errors)) return undefined;
  return (errors as Array<Record<string, unknown>>).map((error) => ({
    field: String(error['path'] ?? ''),
    message: String(error['message'] ?? ''),
  }));
}

function defaultErrorCodeForStatus(status: number): string {
  const knownCodes: Record<number, string> = {
    400: 'BAD_REQUEST',
    401: 'UNAUTHORIZED',
    403: 'FORBIDDEN',
    404: 'NOT_FOUND',
    409: 'CONFLICT',
    422: 'UNPROCESSABLE_ENTITY',
    429: 'RATE_LIMITED',
    502: 'BAD_GATEWAY',
    503: 'SERVICE_UNAVAILABLE',
  };
  return knownCodes[status] ?? (status >= 500 ? 'INTERNAL_ERROR' : 'ERROR');
}
