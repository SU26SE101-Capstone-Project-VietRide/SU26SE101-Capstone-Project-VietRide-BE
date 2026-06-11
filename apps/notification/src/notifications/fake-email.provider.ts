import { Injectable } from '@nestjs/common';
import type { EmailProvider, EmailProviderPayload, EmailProviderResult } from './email-send.types';

@Injectable()
export class FakeEmailProvider implements EmailProvider {
  async send(payload: EmailProviderPayload): Promise<EmailProviderResult> {
    return { messageId: `fake-email:${payload.toEmail}` };
  }
}
