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
 * Global exception filter that converts every thrown error into the unified
 * `ApiResponse<T>` error envelope per ADR 0004.
 *
 * Shape: { success: false, statusCode, error: { code, message, fields? }, meta }
 *
 * Replaces the RFC 7807 `ProblemDetailsExceptionFilter`.  Preserves HTTP
 * status codes + `error.code` from the BSOT section 5.9 registry.
 */
@Catch()
export class ApiResponseExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger('ApiResponseExceptionFilter');

  catch(exception: unknown, host: ArgumentsHost): void {
    const ctx = host.switchToHttp();
    const res = ctx.getResponse<Response>();
    const req = ctx.getRequest<Request>();

    const status =
      exception instanceof HttpException ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;

    const raw = exception instanceof HttpException ? exception.getResponse() : undefined;

    const errorCode = extractErrorCode(raw, status);
    const message = extractMessage(raw, exception);
    const fields = extractFields(raw);

    const traceId =
      (req.headers['x-request-id'] as string | undefined) ??
      (req as { requestId?: string }).requestId;

    const envelope = {
      success: false as const,
      statusCode: status,
      error: {
        code: errorCode,
        message,
        ...(fields ? { fields } : {}),
      },
      meta: {
        traceId,
        timestamp: new Date().toISOString(),
      },
    };

    if (status >= 500) {
      this.logger.error(
        `${req.method} ${req.originalUrl ?? req.url} -> ${status} ${errorCode}: ${message}`,
        exception instanceof Error ? exception.stack : undefined,
      );
    } else {
      this.logger.warn(`${req.method} ${req.originalUrl ?? req.url} -> ${status} ${errorCode}`);
    }

    if (!res.headersSent) {
      res.status(status).json(envelope);
    }
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function extractErrorCode(raw: unknown, status: number): string {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (typeof obj['errorCode'] === 'string') return obj['errorCode'];
  }
  return defaultErrorCodeForStatus(status);
}

function extractMessage(raw: unknown, exception: unknown): string {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (typeof obj['message'] === 'string') return obj['message'];
    if (Array.isArray(obj['message'])) return (obj['message'] as unknown[]).join('; ');
    if (typeof obj['detail'] === 'string') return obj['detail'];
  }
  if (typeof raw === 'string') return raw;
  if (exception instanceof Error) return exception.message;
  return 'Unexpected error';
}

/**
 * Extract field-level validation errors.  ZodValidationPipe emits them under
 * `errors` (array of `{ path, code, message }`).  We remap to the envelope's
 * `error.fields[]` shape (`{ field, message }`).
 */
function extractFields(raw: unknown): Array<{ field: string; message: string }> | undefined {
  if (raw && typeof raw === 'object') {
    const obj = raw as Record<string, unknown>;
    if (Array.isArray(obj['errors'])) {
      return (obj['errors'] as Array<Record<string, unknown>>).map((e) => ({
        field: String(e['path'] ?? ''),
        message: String(e['message'] ?? ''),
      }));
    }
  }
  return undefined;
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
      return 'RATE_LIMITED';
    case 502:
      return 'BAD_GATEWAY';
    case 503:
      return 'SERVICE_UNAVAILABLE';
    default:
      return status >= 500 ? 'INTERNAL_ERROR' : 'ERROR';
  }
}
