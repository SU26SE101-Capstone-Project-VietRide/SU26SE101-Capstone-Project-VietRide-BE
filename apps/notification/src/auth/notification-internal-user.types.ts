import type { Request } from 'express';

export interface NotificationInternalUser {
  sub: string;
  role?: string;
  operatorId?: string;
  reqId?: string;
}

export interface RequestWithNotificationInternalUser extends Request {
  user?: NotificationInternalUser;
}
