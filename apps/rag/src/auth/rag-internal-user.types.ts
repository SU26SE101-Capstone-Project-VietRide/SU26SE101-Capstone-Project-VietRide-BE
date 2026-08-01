import type { Request } from 'express';

export interface RagInternalUser {
  sub: string;
  role?: string;
  operatorId?: string;
  reqId?: string;
}

export interface RequestWithRagInternalUser extends Request {
  user?: RagInternalUser;
  idempotencyOperationId?: string;
  rawBody?: Buffer;
}
