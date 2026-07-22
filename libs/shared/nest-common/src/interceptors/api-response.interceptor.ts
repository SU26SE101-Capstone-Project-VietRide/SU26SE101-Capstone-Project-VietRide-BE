import { CallHandler, ExecutionContext, Injectable, NestInterceptor } from '@nestjs/common';
import type { Request, Response } from 'express';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

/**
 * Global interceptor that auto-wraps any successful HTTP response into the
 * unified `ApiResponse<T>` envelope per ADR 0004.
 *
 * - 204 No Content is left empty (no envelope).
 * - Values that are already shaped as `{ success, statusCode, ... }` are
 *   passed through unchanged to avoid double-wrapping.
 * - Non-HTTP contexts (WebSockets, GraphQL, microservices) are skipped.
 */
@Injectable()
export class ApiResponseInterceptor implements NestInterceptor {
  intercept(context: ExecutionContext, next: CallHandler): Observable<unknown> {
    if (context.getType() !== 'http') {
      return next.handle();
    }

    const http = context.switchToHttp();
    const res = http.getResponse<Response>();
    const req = http.getRequest<Request>();

    if (req.path === '/internal' || req.path.startsWith('/internal/')) {
      return next.handle();
    }

    return next.handle().pipe(
      map((body: unknown) => {
        // 204 — no body
        if (res.statusCode === 204) {
          return body;
        }

        // Skip if already wrapped (e.g. controller explicitly built envelope)
        if (isAlreadyWrapped(body)) {
          return body;
        }

        const traceId =
          (req.headers['x-request-id'] as string | undefined) ??
          (req as { requestId?: string }).requestId;

        return {
          success: true as const,
          statusCode: res.statusCode,
          data: body,
          meta: {
            traceId,
            timestamp: new Date().toISOString(),
          },
        };
      }),
    );
  }
}

/** Detect if the body is already an envelope (has `success` + `statusCode`). */
function isAlreadyWrapped(body: unknown): boolean {
  if (body !== null && typeof body === 'object' && !Array.isArray(body)) {
    const obj = body as Record<string, unknown>;
    return 'success' in obj && 'statusCode' in obj;
  }
  return false;
}
