import { Injectable } from '@nestjs/common';
import type { FcmPushPayload, FcmPushProvider, FcmPushResult } from './fcm-push.types';

@Injectable()
export class FakeFcmPushProvider implements FcmPushProvider {
  async send(payload: FcmPushPayload): Promise<FcmPushResult> {
    return { messageId: `fake-fcm:${payload.token}` };
  }
}
