import {
  CallHandler,
  ExecutionContext,
  HttpException,
  Injectable,
  NestInterceptor,
  UnprocessableEntityException,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { createHash } from 'node:crypto';
import { Observable, catchError, from, mergeMap, of, throwError } from 'rxjs';
import type { Request, Response } from 'express';
import {
  toVietnamIso,
  transformFrontendTimestamps,
  transformUtcTimestamps,
} from '@vietride/nest-common';
import type { RequestWithRagInternalUser } from '../auth/rag-internal-user.types';
import { IDEMPOTENCY_REQUIRED_METADATA } from './idempotency.swagger';
import { IDEMPOTENCY_MULTIPART_DEFERRED_METADATA } from './idempotency.swagger';
import { RagIdempotencyService } from './rag-idempotency.service';

const UUID_V4_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

@Injectable()
export class RagIdempotencyInterceptor implements NestInterceptor {
  constructor(
    private readonly reflector: Reflector,
    private readonly idempotency: RagIdempotencyService,
  ) {}

  async intercept(context: ExecutionContext, next: CallHandler): Promise<Observable<unknown>> {
    const required = this.reflector.getAllAndOverride<boolean>(IDEMPOTENCY_REQUIRED_METADATA, [
      context.getHandler(),
      context.getClass(),
    ]);
    if (!required) return next.handle();
    const multipartDeferred = this.reflector.getAllAndOverride<boolean>(
      IDEMPOTENCY_MULTIPART_DEFERRED_METADATA,
      [context.getHandler(), context.getClass()],
    );
    if (multipartDeferred) return next.handle();

    return this.interceptRequired(context, next);
  }

  protected async interceptRequired(
    context: ExecutionContext,
    next: CallHandler,
  ): Promise<Observable<unknown>> {
    const http = context.switchToHttp();
    const request = http.getRequest<RequestWithRagInternalUser>();
    const response = http.getResponse<Response>();
    const key = this.requireKey(request.headers['idempotency-key']);
    request.idempotencyOperationId = key;
    const userId = request.user?.sub;
    if (!userId) throw new UnprocessableEntityException({ errorCode: 'UNAUTHORIZED' });
    const fingerprint = this.fingerprint(request, userId);
    const begin = await this.idempotency.begin({
      operationId: key,
      userId,
      method: request.method,
      path: request.path,
      fingerprint,
    });
    if (begin.state === 'replay') {
      this.writeReplay(response, begin.response);
      return of(undefined);
    }

    const captured: string[] = [];
    const originalWrite = response.write.bind(response);
    response.write = ((chunk: unknown, ...args: unknown[]) => {
      captured.push(Buffer.isBuffer(chunk) ? chunk.toString('utf8') : String(chunk));
      return originalWrite(chunk as never, ...(args as never[]));
    }) as typeof response.write;

    return next.handle().pipe(
      mergeMap((value) => {
        const normalizedValue = this.normalizeSuccess(value, request, response);
        const replay = {
          statusCode: response.statusCode,
          headers: this.responseHeaders(response, captured.length === 0),
          body: captured.length > 0 ? captured.join('') : JSON.stringify(normalizedValue ?? null),
        };
        return from(
          this.idempotency.complete(
            begin.operationId,
            begin.ownerToken,
            this.canonicalizeReplay(replay),
          ),
        ).pipe(
          mergeMap(() => of(normalizedValue)),
        );
      }),
      catchError((error: unknown) => {
        if (error instanceof HttpException && error.getStatus() < 500) {
          const replay = this.toClientErrorReplay(error, request);
          return from(
            this.idempotency.complete(
              begin.operationId,
              begin.ownerToken,
              this.canonicalizeReplay(replay),
            ),
          ).pipe(
            mergeMap(() => {
              this.writeReplay(response, replay);
              return of(undefined);
            }),
          );
        }
        return from(this.idempotency.abandon(begin.operationId, begin.ownerToken)).pipe(
          mergeMap(() => throwError(() => error)),
        );
      }),
    );
  }

  private requireKey(value: string | string[] | undefined): string {
    const key = Array.isArray(value) ? undefined : value?.trim().toLowerCase();
    if (!key) {
      throw new UnprocessableEntityException({
        errorCode: 'IDEMPOTENCY_KEY_REQUIRED',
        detail: 'Idempotency-Key header is required',
      });
    }
    if (!UUID_V4_PATTERN.test(key)) {
      throw new UnprocessableEntityException({
        errorCode: 'VALIDATION_ERROR',
        detail: 'Idempotency-Key must be a UUID v4',
      });
    }
    return key;
  }

  private fingerprint(request: Request, userId: string): string {
    const ragRequest = request as RequestWithRagInternalUser;
    const upload = (
      request as Request & {
        file?: { originalname: string; mimetype: string; size: number; buffer: Buffer };
      }
    ).file;
    const body = ragRequest.rawBody
      ? Buffer.from(ragRequest.rawBody)
      : Buffer.from(
          JSON.stringify(
            upload
              ? {
                  body: request.body,
                  file: {
                    originalName: upload.originalname,
                    mimeType: upload.mimetype,
                    size: upload.size,
                    sha256: createHash('sha256').update(upload.buffer).digest('hex'),
                  },
                }
              : (request.body ?? null),
          ),
          'utf8',
        );
    const hash = createHash('sha256');
    for (const part of [
      Buffer.from(userId, 'utf8'),
      Buffer.from(request.method.toUpperCase(), 'utf8'),
      Buffer.from(`${request.baseUrl}${request.path}`, 'utf8'),
      this.canonicalQuery(request),
      body,
    ]) {
      const length = Buffer.allocUnsafe(4);
      length.writeUInt32BE(part.length);
      hash.update(length);
      hash.update(part);
    }
    return hash.digest('hex').toUpperCase();
  }

  private canonicalQuery(request: Request): Buffer {
    const search = new URL(request.originalUrl, 'http://vietride.internal').searchParams;
    const entries = [...search.entries()].sort(([leftKey, leftValue], [rightKey, rightValue]) => {
      if (leftKey !== rightKey) return leftKey < rightKey ? -1 : 1;
      if (leftValue === rightValue) return 0;
      return leftValue < rightValue ? -1 : 1;
    });
    const parts: Buffer[] = [];
    const count = Buffer.allocUnsafe(4);
    count.writeUInt32BE(entries.length);
    parts.push(count);
    for (const [key, value] of entries) {
      for (const item of [key, value]) {
        const encoded = Buffer.from(item, 'utf8');
        const length = Buffer.allocUnsafe(4);
        length.writeUInt32BE(encoded.length);
        parts.push(length, encoded);
      }
    }
    return Buffer.concat(parts);
  }

  private responseHeaders(response: Response, defaultJson: boolean): Record<string, string> {
    const result: Record<string, string> = {};
    for (const name of ['content-type', 'cache-control']) {
      const value = response.getHeader(name);
      if (typeof value === 'string') result[name] = value;
    }
    if (defaultJson && !result['content-type']) {
      result['content-type'] = 'application/json; charset=utf-8';
    }
    return result;
  }

  protected normalizeSuccess(value: unknown, _request: Request, _response: Response): unknown {
    void _request;
    void _response;
    return value;
  }

  private writeReplay(
    response: Response,
    replay: { statusCode: number; headers: Record<string, string>; body: string },
  ): void {
    const presented = this.presentReplay(replay);
    response.status(presented.statusCode);
    for (const [name, value] of Object.entries(presented.headers)) response.setHeader(name, value);
    response.end(presented.body);
  }

  private canonicalizeReplay<T extends {
    statusCode: number;
    headers: Record<string, string>;
    body: string;
  }>(replay: T): T {
    return this.transformReplayJson(replay, transformUtcTimestamps);
  }

  private presentReplay<T extends {
    statusCode: number;
    headers: Record<string, string>;
    body: string;
  }>(replay: T): T {
    return this.transformReplayJson(replay, transformFrontendTimestamps);
  }

  private transformReplayJson<T extends {
    statusCode: number;
    headers: Record<string, string>;
    body: string;
  }>(replay: T, transform: <V>(value: V) => V): T {
    const contentType = Object.entries(replay.headers).find(
      ([name]) => name.toLowerCase() === 'content-type',
    )?.[1];
    if (!contentType?.toLowerCase().includes('json') || replay.body.length === 0) {
      return replay;
    }

    try {
      return { ...replay, body: JSON.stringify(transform(JSON.parse(replay.body))) };
    } catch {
      return replay;
    }
  }

  private toClientErrorReplay(
    error: HttpException,
    request: Request,
  ): { statusCode: number; headers: Record<string, string>; body: string } {
    const statusCode = error.getStatus();
    const raw = error.getResponse();
    const object = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : undefined;
    const code =
      typeof object?.['errorCode'] === 'string'
        ? object['errorCode']
        : statusCode === 400
          ? 'BAD_REQUEST'
          : statusCode === 403
            ? 'FORBIDDEN'
            : statusCode === 404
              ? 'NOT_FOUND'
              : statusCode === 409
                ? 'CONFLICT'
                : statusCode === 422
                  ? 'UNPROCESSABLE_ENTITY'
                  : 'ERROR';
    const message =
      typeof object?.['message'] === 'string'
        ? object['message']
        : typeof object?.['detail'] === 'string'
          ? object['detail']
          : typeof raw === 'string'
            ? raw
            : error.message;
    const errors = Array.isArray(object?.['errors'])
      ? (object['errors'] as Array<Record<string, unknown>>).map((item) => ({
          field: String(item['path'] ?? ''),
          message: String(item['message'] ?? ''),
        }))
      : undefined;
    const traceId =
      (request.headers['x-request-id'] as string | undefined) ??
      (request as { requestId?: string }).requestId;
    return {
      statusCode,
      headers: { 'content-type': 'application/json; charset=utf-8' },
      body: JSON.stringify({
        success: false,
        statusCode,
        error: { code, message, ...(errors ? { fields: errors } : {}) },
        meta: { traceId, timestamp: toVietnamIso(new Date()) },
      }),
    };
  }
}

@Injectable()
export class RagMultipartIdempotencyInterceptor extends RagIdempotencyInterceptor {
  override intercept(context: ExecutionContext, next: CallHandler): Promise<Observable<unknown>> {
    return this.interceptRequired(context, next);
  }

  protected override normalizeSuccess(
    value: unknown,
    request: Request,
    response: Response,
  ): unknown {
    if (
      response.statusCode === 204 ||
      (value !== null &&
        typeof value === 'object' &&
        !Array.isArray(value) &&
        'success' in value &&
        'statusCode' in value)
    ) {
      return value;
    }
    const traceId =
      (request.headers['x-request-id'] as string | undefined) ??
      (request as { requestId?: string }).requestId;
    return transformFrontendTimestamps({
      success: true,
      statusCode: response.statusCode,
      data: value,
      meta: { traceId, timestamp: toVietnamIso(new Date()) },
    });
  }
}
