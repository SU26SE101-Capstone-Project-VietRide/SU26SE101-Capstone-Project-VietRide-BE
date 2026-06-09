import type { DevicePlatform } from '../generated/notification-prisma-client';

export interface FcmPushJobData {
  notificationId: string;
  userId: string;
}

export interface DeviceTokenSnapshot {
  fcmToken: string;
  platform: DevicePlatform;
}

export interface FcmPushPayload {
  token: string;
  title: string;
  body: string;
  data: Record<string, string>;
}

export interface FcmPushResult {
  messageId?: string;
  invalidToken?: boolean;
}

export interface FcmPushProvider {
  send(payload: FcmPushPayload): Promise<FcmPushResult>;
}

export interface DeviceTokenProvider {
  listActiveDeviceTokens(userId: string): Promise<DeviceTokenSnapshot[]>;
}
