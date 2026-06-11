import type { Request } from 'express';

export interface NotificationUser {
  userId: string;
  role: string;
  operatorId?: string;
}

export interface RequestWithNotificationUser extends Request {
  user?: NotificationUser;
}
