import { Inject, Injectable } from '@nestjs/common';
import sendGridMail from '@sendgrid/mail';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import type { EmailProvider, EmailProviderPayload, EmailProviderResult } from './email-send.types';

const SENDGRID_MESSAGE_ID_HEADER = 'x-message-id';

@Injectable()
export class SendGridEmailProvider implements EmailProvider {
  constructor(@Inject(ENV_TOKEN) private readonly env: Env) {}

  async send(payload: EmailProviderPayload): Promise<EmailProviderResult> {
    if (!this.env.SENDGRID_API_KEY || !this.env.SENDGRID_FROM_EMAIL) {
      throw new Error('SENDGRID_NOT_CONFIGURED');
    }

    sendGridMail.setApiKey(this.env.SENDGRID_API_KEY);
    const [response] = await sendGridMail.send({
      to: payload.toEmail,
      from: {
        email: this.env.SENDGRID_FROM_EMAIL,
        name: this.env.SENDGRID_FROM_NAME,
      },
      subject: payload.subject,
      text: payload.text,
      html: payload.html,
      customArgs: { vietride_delivery_id: payload.deliveryId },
    });

    const messageId = this.extractMessageId(response.headers);
    return messageId ? { messageId } : {};
  }

  private extractMessageId(headers: Record<string, unknown>): string | undefined {
    const value = headers[SENDGRID_MESSAGE_ID_HEADER];
    return typeof value === 'string' ? value : undefined;
  }
}
