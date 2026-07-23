import {
  CallHandler,
  ExecutionContext,
  Injectable,
  NestInterceptor,
  UnprocessableEntityException,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { createHash } from 'node:crypto';
import { Observable, catchError, from, mergeMap, of, throwError } from 'rxjs';
import type { Request, Response } from 'express';
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
      response.status(begin.response.statusCode);
      for (const [name, value] of Object.entries(begin.response.headers)) {
        response.setHeader(name, value);
      }
      if (begin.response.headers['content-type']?.includes('text/event-stream')) {
        response.write(begin.response.body);
        response.end();
        return of(undefined);
      }
      return of(begin.response.body ? JSON.parse(begin.response.body) : undefined);
    }

    const captured: string[] = [];
    const originalWrite = response.write.bind(response);
    response.write = ((chunk: unknown, ...args: unknown[]) => {
      captured.push(Buffer.isBuffer(chunk) ? chunk.toString('utf8') : String(chunk));
      return originalWrite(chunk as never, ...(args as never[]));
    }) as typeof response.write;

    return next.handle().pipe(
      mergeMap((value) =>
        from(
          this.idempotency.complete(begin.operationId, begin.ownerToken, {
            statusCode: response.statusCode,
            headers: this.responseHeaders(response),
            body: captured.length > 0 ? captured.join('') : JSON.stringify(value ?? null),
          }),
        ).pipe(mergeMap(() => of(value))),
      ),
      catchError((error: unknown) =>
        from(this.idempotency.abandon(begin.operationId, begin.ownerToken)).pipe(
          mergeMap(() => throwError(() => error)),
        ),
      ),
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
    const upload = (
      request as Request & {
        file?: { originalname: string; mimetype: string; size: number; buffer: Buffer };
      }
    ).file;
    return createHash('sha256')
      .update(
        JSON.stringify({
          userId,
          method: request.method,
          path: request.path,
          query: request.query,
          body: request.body,
          file: upload
            ? {
                originalName: upload.originalname,
                mimeType: upload.mimetype,
                size: upload.size,
                sha256: createHash('sha256').update(upload.buffer).digest('hex'),
              }
            : null,
        }),
      )
      .digest('hex')
      .toUpperCase();
  }

  private responseHeaders(response: Response): Record<string, string> {
    const result: Record<string, string> = {};
    for (const name of ['content-type', 'cache-control']) {
      const value = response.getHeader(name);
      if (typeof value === 'string') result[name] = value;
    }
    return result;
  }
}

@Injectable()
export class RagMultipartIdempotencyInterceptor extends RagIdempotencyInterceptor {
  override intercept(context: ExecutionContext, next: CallHandler): Promise<Observable<unknown>> {
    return this.interceptRequired(context, next);
  }
}
