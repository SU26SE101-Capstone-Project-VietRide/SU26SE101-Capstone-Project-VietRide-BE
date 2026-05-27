import {
  CallHandler,
  ExecutionContext,
  Injectable,
  NestInterceptor,
} from '@nestjs/common';
import type { Request, Response } from 'express';
import pino, { type Logger as PinoLogger } from 'pino';
import { Observable, tap } from 'rxjs';

/**
 * Logs inbound HTTP requests + their duration / status using pino.
 *
 * Designed as a singleton (one pino instance per process). To reuse an
 * existing pino logger from elsewhere, construct with `new LoggingInterceptor(myLogger)`.
 */
@Injectable()
export class LoggingInterceptor implements NestInterceptor {
  private readonly logger: PinoLogger;

  constructor(logger?: PinoLogger) {
    this.logger = logger ?? pino({ name: 'http', level: process.env['LOG_LEVEL'] ?? 'info' });
  }

  intercept(context: ExecutionContext, next: CallHandler): Observable<unknown> {
    if (context.getType() !== 'http') {
      return next.handle();
    }
    const http = context.switchToHttp();
    const req = http.getRequest<Request>();
    const res = http.getResponse<Response>();
    const start = process.hrtime.bigint();
    const requestId = (req.headers['x-request-id'] as string | undefined)
      ?? (req as { requestId?: string }).requestId;

    const baseFields = {
      method: req.method,
      url: req.originalUrl ?? req.url,
      requestId,
    };

    this.logger.info(baseFields, 'request.start');

    return next.handle().pipe(
      tap({
        next: () => this.emit(baseFields, res.statusCode, start, 'request.end'),
        error: (err) => this.emit(baseFields, res.statusCode ?? 500, start, 'request.error', err),
      }),
    );
  }

  private emit(
    base: Record<string, unknown>,
    status: number,
    start: bigint,
    msg: string,
    err?: unknown,
  ): void {
    const durMs = Number(process.hrtime.bigint() - start) / 1_000_000;
    const fields = { ...base, status, durMs: Math.round(durMs * 1000) / 1000 };
    if (err) {
      this.logger.error({ ...fields, err: err instanceof Error ? err.message : String(err) }, msg);
    } else if (status >= 500) {
      this.logger.error(fields, msg);
    } else if (status >= 400) {
      this.logger.warn(fields, msg);
    } else {
      this.logger.info(fields, msg);
    }
  }
}
