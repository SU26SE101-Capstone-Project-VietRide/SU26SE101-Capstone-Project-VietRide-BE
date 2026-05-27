import { Inject, Injectable, Scope } from '@nestjs/common';
import { REQUEST } from '@nestjs/core';
import type { Request } from 'express';

/**
 * Request-scoped accessor for correlation id + authenticated user metadata.
 * Populated by CorrelationIdMiddleware (requestId) and the gateway's
 * user-jwt middleware (userId / role).
 */
@Injectable({ scope: Scope.REQUEST })
export class RequestContextService {
  constructor(@Inject(REQUEST) private readonly req: Request) {}

  get requestId(): string | undefined {
    return (this.req as RequestLike).requestId
      ?? (this.req.headers['x-request-id'] as string | undefined);
  }

  get userId(): string | undefined {
    const user = (this.req as RequestLike).user;
    return user?.sub ?? user?.userId;
  }

  get role(): string | undefined {
    const user = (this.req as RequestLike).user;
    return user?.role;
  }

  get operatorId(): string | undefined {
    const user = (this.req as RequestLike).user;
    return user?.operatorId;
  }
}

interface RequestLike extends Request {
  requestId?: string;
  user?: {
    sub?: string;
    userId?: string;
    role?: string;
    operatorId?: string;
    [k: string]: unknown;
  };
}
