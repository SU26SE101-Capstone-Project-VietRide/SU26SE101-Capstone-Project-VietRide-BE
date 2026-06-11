import { Inject, Injectable } from '@nestjs/common';
import { applicationDefault, cert, getApps, initializeApp, type App, type AppOptions } from 'firebase-admin/app';
import { getMessaging } from 'firebase-admin/messaging';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { FcmPushPayload, FcmPushProvider, FcmPushResult } from './fcm-push.types';

const INVALID_FCM_ERROR_CODES = new Set([
  'messaging/invalid-argument',
  'messaging/invalid-registration-token',
  'messaging/registration-token-not-registered',
]);

@Injectable()
export class FirebaseFcmPushProvider implements FcmPushProvider {
  private readonly app: App;

  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {
    this.app = getApps()[0] ?? initializeApp(this.buildFirebaseOptions());
  }

  async send(payload: FcmPushPayload): Promise<FcmPushResult> {
    try {
      const messageId = await getMessaging(this.app).send({
        token: payload.token,
        notification: {
          title: payload.title,
          body: payload.body,
        },
        data: payload.data,
      });

      return { messageId };
    } catch (error) {
      if (this.isInvalidTokenError(error)) {
        return { invalidToken: true };
      }

      throw error;
    }
  }

  private buildFirebaseOptions(): AppOptions {
    if (this.env.FCM_PROJECT_ID && this.env.FCM_CLIENT_EMAIL && this.env.FCM_PRIVATE_KEY) {
      return {
        credential: cert({
          projectId: this.env.FCM_PROJECT_ID,
          clientEmail: this.env.FCM_CLIENT_EMAIL,
          privateKey: this.env.FCM_PRIVATE_KEY.replace(/\\n/g, '\n'),
        }),
        projectId: this.env.FCM_PROJECT_ID,
      };
    }

    const options: AppOptions = {
      credential: applicationDefault(),
    };
    if (this.env.FCM_PROJECT_ID) {
      options.projectId = this.env.FCM_PROJECT_ID;
    }

    return options;
  }

  private isInvalidTokenError(error: unknown): boolean {
    if (typeof error !== 'object' || error === null || !('code' in error)) {
      return false;
    }

    return INVALID_FCM_ERROR_CODES.has(String(error.code));
  }
}
