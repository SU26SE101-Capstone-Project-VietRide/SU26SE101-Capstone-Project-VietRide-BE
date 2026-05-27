import { Injectable, NestMiddleware } from '@nestjs/common';
import type { NextFunction, Request, Response } from 'express';
import { randomUUID } from 'node:crypto';

/**
 * Reads `x-request-id` from inbound headers (or generates a v4 UUID),
 * stamps it onto `req.requestId` and echoes it back via the response
 * header so callers can correlate logs end-to-end.
 *
 * Per BACKEND_SOURCE_OF_TRUTH 3.4.2 middleware chain.
 */
@Injectable()
export class CorrelationIdMiddleware implements NestMiddleware {
  use(req: Request, res: Response, next: NextFunction): void {
    const header = req.header('x-request-id');
    const requestId = (header && header.trim().length > 0 ? header.trim() : randomUUID());
    (req as RequestWithCorrelationId).requestId = requestId;
    req.headers['x-request-id'] = requestId;
    res.setHeader('X-Request-Id', requestId);
    next();
  }
}

export interface RequestWithCorrelationId extends Request {
  requestId?: string;
}
