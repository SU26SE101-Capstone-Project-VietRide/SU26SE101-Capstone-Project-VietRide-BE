import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
  HttpStatus,
  Logger,
} from '@nestjs/common';
import type { Request, Response } from 'express';

/**
 * Global exception filter that converts every thrown error into an
 * RFC 7807 Problem Details document.
 *
 * Shape: { type, title, status, detail, instance, traceId, errorCode?, errors? }
 *
 * Per VietRide_API_Contract_v1 §"Error envelope" + BACKEND_SOURCE_OF_TRUTH 3.4.2 / §5.5.
 */
@Catch()
export class ProblemDetailsExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger('ProblemDetailsExceptionFilter');

  constructor(private readonly baseTypeUrl = 'https://vietride.app/errors') {}

  catch(exception: unknown, host: ArgumentsHost): void {
    const ctx = host.switchToHttp();
    const res = ctx.getResponse<Response>();
    const req = ctx.getRequest<Request>();

    const status =
      exception instanceof HttpException ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;

    const raw = exception instanceof HttpException ? exception.getResponse() : undefined;
    const errorCode = extractErrorCode(raw, status);
    const title = extractTitle(raw, status);
    const detail = extractDetail(raw, exception);

    const traceId =
      (req.headers['x-request-id'] as string | undefined) ?? (req as RequestLike).requestId;

    // Field-level validation detail (e.g. from ZodValidationPipe) — forwarded as `errors`
    // per BACKEND_SOURCE_OF_TRUTH §5.5 ProblemDetails shape.
    const errors = extractErrors(raw);

    const problem = {
      type: `${this.baseTypeUrl}/${errorCode}`,
      title,
      status,
      detail,
      instance: req.originalUrl ?? req.url,
      traceId,
      errorCode,
      ...(errors ? { errors } : {}),
    };

    if (status >= 500) {
      this.logger.error(
        `${req.method} ${problem.instance} -> ${status} ${errorCode}: ${detail}`,
        exception instanceof Error ? exception.stack : undefined,
      );
    } else {
      this.logger.warn(`${req.method} ${problem.instance} -> ${status} ${errorCode}`);
    }

    if (!res.headersSent) {
      res.status(status).json(problem);
    }
  }
}

interface RequestLike extends Request {
  requestId?: string;
}

/** Pull a field-level `errors` array off the thrown HttpException body, if present. */
function extractErrors(raw: unknown): unknown[] | undefined {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (Array.isArray(obj['errors'])) return obj['errors'] as unknown[];
  }
  return undefined;
}

function extractErrorCode(raw: unknown, status: number): string {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (typeof obj['errorCode'] === 'string') return obj['errorCode'] as string;
  }
  return defaultErrorCodeForStatus(status);
}

function extractTitle(raw: unknown, status: number): string {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (typeof obj['title'] === 'string') return obj['title'] as string;
    if (typeof obj['error'] === 'string') return obj['error'] as string;
  }
  return defaultTitleForStatus(status);
}

function extractDetail(raw: unknown, exception: unknown): string {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (typeof obj['detail'] === 'string') return obj['detail'] as string;
    if (typeof obj['message'] === 'string') return obj['message'] as string;
    if (Array.isArray(obj['message'])) return (obj['message'] as unknown[]).join('; ');
  }
  if (typeof raw === 'string') return raw;
  if (exception instanceof Error) return exception.message;
  return 'Unexpected error';
}

function defaultTitleForStatus(status: number): string {
  switch (status) {
    case 400:
      return 'Bad Request';
    case 401:
      return 'Unauthorized';
    case 403:
      return 'Forbidden';
    case 404:
      return 'Not Found';
    case 409:
      return 'Conflict';
    case 422:
      return 'Unprocessable Entity';
    case 429:
      return 'Too Many Requests';
    case 502:
      return 'Bad Gateway';
    case 503:
      return 'Service Unavailable';
    default:
      return status >= 500 ? 'Internal Server Error' : 'Error';
  }
}

function defaultErrorCodeForStatus(status: number): string {
  switch (status) {
    case 400:
      return 'BAD_REQUEST';
    case 401:
      return 'UNAUTHORIZED';
    case 403:
      return 'FORBIDDEN';
    case 404:
      return 'NOT_FOUND';
    case 409:
      return 'CONFLICT';
    case 422:
      return 'UNPROCESSABLE_ENTITY';
    case 429:
      return 'TOO_MANY_REQUESTS';
    case 502:
      return 'BAD_GATEWAY';
    case 503:
      return 'SERVICE_UNAVAILABLE';
    default:
      return status >= 500 ? 'INTERNAL_ERROR' : 'ERROR';
  }
}
