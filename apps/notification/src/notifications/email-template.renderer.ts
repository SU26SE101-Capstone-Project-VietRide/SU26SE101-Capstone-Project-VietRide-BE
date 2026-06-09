import { Injectable } from '@nestjs/common';
import { EmailTemplateKey } from '../generated/notification-prisma-client';
import type { EmailTemplateData, RenderedEmail } from './email-send.types';

const DEFAULT_TEXT = '';

@Injectable()
export class EmailTemplateRenderer {
  render(templateKey: EmailTemplateKey, data: EmailTemplateData): RenderedEmail {
    switch (templateKey) {
      case EmailTemplateKey.AUTH_OTP:
        return this.renderAuthOtp(data);
      case EmailTemplateKey.SET_INITIAL_PASSWORD:
        return this.renderSetInitialPassword(data);
      case EmailTemplateKey.PARCEL_DELIVERY_LINK:
        return this.renderParcelDeliveryLink(data);
      case EmailTemplateKey.OPERATOR_SUBSCRIPTION_NOTICE:
        return this.renderOperatorSubscriptionNotice(data);
      case EmailTemplateKey.INVOICE_NOTICE:
        return this.renderInvoiceNotice(data);
      default:
        return this.renderGenericNotice(data);
    }
  }

  private renderAuthOtp(data: EmailTemplateData): RenderedEmail {
    const otpCode = this.requiredString(data, 'otpCode');
    const ttlMinutes = this.optionalString(data, 'ttlMinutes') ?? '10';
    const purpose = this.optionalString(data, 'purpose') ?? 'xac thuc';
    const subject = 'Ma xac thuc VietRide';
    const text = `Ma ${purpose} cua ban la ${otpCode}. Ma co hieu luc trong ${ttlMinutes} phut.`;

    return {
      subject,
      text,
      html: this.paragraphs(subject, [
        `Ma ${this.escapeHtml(purpose)} cua ban la <strong>${this.escapeHtml(otpCode)}</strong>.`,
        `Ma co hieu luc trong ${this.escapeHtml(ttlMinutes)} phut.`,
      ]),
    };
  }

  private renderSetInitialPassword(data: EmailTemplateData): RenderedEmail {
    const setPasswordUrl = this.requiredString(data, 'setPasswordUrl');
    const subject = 'Thiet lap mat khau VietRide';
    const text = `Tai khoan VietRide cua ban da duoc tao. Thiet lap mat khau tai: ${setPasswordUrl}`;

    return {
      subject,
      text,
      html: this.paragraphs(subject, [
        'Tai khoan VietRide cua ban da duoc tao.',
        `<a href="${this.escapeHtml(setPasswordUrl)}">Thiet lap mat khau</a>`,
      ]),
    };
  }

  private renderParcelDeliveryLink(data: EmailTemplateData): RenderedEmail {
    const deliveryUrl = this.requiredString(data, 'deliveryUrl');
    const parcelCode = this.optionalString(data, 'parcelCode') ?? 'kien hang';
    const subject = 'Xac nhan giao hang VietRide';
    const text = `Vui long xac nhan giao ${parcelCode} tai: ${deliveryUrl}`;

    return {
      subject,
      text,
      html: this.paragraphs(subject, [
        `Vui long xac nhan giao ${this.escapeHtml(parcelCode)}.`,
        `<a href="${this.escapeHtml(deliveryUrl)}">Mo lien ket xac nhan</a>`,
      ]),
    };
  }

  private renderOperatorSubscriptionNotice(data: EmailTemplateData): RenderedEmail {
    const message = this.requiredString(data, 'message');
    const title = this.optionalString(data, 'title') ?? 'Thong bao goi dich vu VietRide';
    const actionUrl = this.optionalString(data, 'actionUrl');
    const paragraphs = [this.escapeHtml(message)];
    if (actionUrl) {
      paragraphs.push(`<a href="${this.escapeHtml(actionUrl)}">Xem chi tiet</a>`);
    }

    return {
      subject: title,
      text: actionUrl ? `${message}\n${actionUrl}` : message,
      html: this.paragraphs(title, paragraphs),
    };
  }

  private renderInvoiceNotice(data: EmailTemplateData): RenderedEmail {
    const invoiceNumber = this.optionalString(data, 'invoiceNumber') ?? 'hoa don moi';
    const amountVnd = this.optionalString(data, 'amountVnd');
    const invoiceUrl = this.optionalString(data, 'invoiceUrl');
    const subject = `Hoa don VietRide ${invoiceNumber}`;
    const lines = [`Hoa don ${invoiceNumber} da san sang.`];
    if (amountVnd) {
      lines.push(`So tien: ${amountVnd} VND.`);
    }
    if (invoiceUrl) {
      lines.push(invoiceUrl);
    }

    return {
      subject,
      text: lines.join('\n'),
      html: this.paragraphs(subject, lines.map((line) => this.escapeHtml(line))),
    };
  }

  private renderGenericNotice(data: EmailTemplateData): RenderedEmail {
    const subject = this.optionalString(data, 'title') ?? 'Thong bao VietRide';
    const body = this.optionalString(data, 'message') ?? DEFAULT_TEXT;

    return {
      subject,
      text: body,
      html: this.paragraphs(subject, [this.escapeHtml(body)]),
    };
  }

  private paragraphs(title: string, paragraphs: string[]): string {
    const body = paragraphs.map((paragraph) => `<p>${paragraph}</p>`).join('');
    return `<h1>${this.escapeHtml(title)}</h1>${body}`;
  }

  private requiredString(data: EmailTemplateData, key: string): string {
    const value = this.optionalString(data, key);
    if (!value) {
      throw new Error(`EMAIL_TEMPLATE_MISSING_${key.toUpperCase()}`);
    }

    return value;
  }

  private optionalString(data: EmailTemplateData, key: string): string | null {
    const value = data[key];
    if (value === null || value === undefined) {
      return null;
    }

    return typeof value === 'string' ? value : String(value);
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }
}
