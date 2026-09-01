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
    // Identity posts `code`; older callers/tests use `otpCode`. Accept both.
    const otpCode = this.requiredString(data, 'otpCode', 'code');
    const ttlMinutes = this.optionalString(data, 'ttlMinutes') ?? '10';
    const purpose = this.resolveOtpPurpose(this.optionalString(data, 'purpose'));
    const subject = 'Mã xác thực VietRide';
    const text = `Mã ${purpose} của bạn là ${otpCode}. Mã có hiệu lực trong ${ttlMinutes} phút.`;

    return {
      subject,
      text,
      html: this.paragraphs(subject, [
        `Mã ${this.escapeHtml(purpose)} của bạn là <strong>${this.escapeHtml(otpCode)}</strong>.`,
        `Mã có hiệu lực trong ${this.escapeHtml(ttlMinutes)} phút.`,
      ]),
    };
  }

  private renderSetInitialPassword(data: EmailTemplateData): RenderedEmail {
    // Identity posts `setInitialPasswordUrl`; older callers use `setPasswordUrl`.
    const setPasswordUrl = this.requiredString(data, 'setPasswordUrl', 'setInitialPasswordUrl');
    const subject = 'Thiết lập mật khẩu VietRide';
    const text = `Tài khoản VietRide của bạn đã được tạo. Thiết lập mật khẩu tại: ${setPasswordUrl}`;

    return {
      subject,
      text,
      html: this.paragraphs(subject, [
        'Tài khoản VietRide của bạn đã được tạo.',
        `<a href="${this.escapeHtml(setPasswordUrl)}">Thiết lập mật khẩu</a>`,
      ]),
    };
  }

 private renderParcelDeliveryLink(data: EmailTemplateData): RenderedEmail {
  const deliveryUrl = this.requiredString(data, 'deliveryUrl');
  const parcelCode = this.optionalString(data, 'parcelCode') ?? 'kiện hàng';
  const subject = 'Xác nhận giao hàng VietRide';
  const text = `Vui lòng xác nhận giao ${parcelCode} tại: ${deliveryUrl}`;
  const escapedDeliveryUrl = this.escapeHtml(deliveryUrl);

  return {
    subject,
    text,
    html: this.paragraphs(subject, [
      `Vui lòng xác nhận giao ${this.escapeHtml(parcelCode)}.`,
      `<a href="${escapedDeliveryUrl}">Xác nhận đã nhận hàng</a>`,
      'Nếu nút không hoạt động, hãy mở hoặc sao chép đường dẫn bên dưới:',
      `<a href="${escapedDeliveryUrl}" style="word-break: break-all;">${escapedDeliveryUrl}</a>`,
    ]),
  };
}

  private renderOperatorSubscriptionNotice(data: EmailTemplateData): RenderedEmail {
    const message = this.requiredString(data, 'message');
    const title = this.optionalString(data, 'title') ?? 'Thông báo gói dịch vụ VietRide';
    const actionUrl = this.optionalString(data, 'actionUrl');
    const paragraphs = [this.escapeHtml(message)];
    if (actionUrl) {
      paragraphs.push(`<a href="${this.escapeHtml(actionUrl)}">Xem chi tiết</a>`);
    }

    return {
      subject: title,
      text: actionUrl ? `${message}\n${actionUrl}` : message,
      html: this.paragraphs(title, paragraphs),
    };
  }

  private renderInvoiceNotice(data: EmailTemplateData): RenderedEmail {
    const invoiceNumber = this.optionalString(data, 'invoiceNumber') ?? 'hóa đơn mới';
    const amountVnd = this.optionalString(data, 'amountVnd');
    const invoiceUrl = this.optionalString(data, 'invoiceUrl');
    const subject = `Hóa đơn VietRide ${invoiceNumber}`;
    const firstLine = `Hóa đơn ${invoiceNumber} đã sẵn sàng.`;
    const lines = [firstLine];
    if (amountVnd) {
      lines.push(`Số tiền: ${amountVnd} VND.`);
    }
    if (invoiceUrl) {
      lines.push(invoiceUrl);
    }

    return {
      subject,
      text: lines.join('\n'),
      html: this.paragraphs(subject, [
        this.escapeHtml(firstLine),
        ...(amountVnd ? [this.escapeHtml(`Số tiền: ${amountVnd} VND.`)] : []),
        ...(invoiceUrl
          ? [`<a href="${this.escapeHtml(invoiceUrl)}">Xem chi tiết hóa đơn</a>`]
          : []),
      ]),
    };
  }

  private renderGenericNotice(data: EmailTemplateData): RenderedEmail {
    const subject = this.optionalString(data, 'title') ?? 'Thông báo VietRide';
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

  private resolveOtpPurpose(rawPurpose: string | null): string {
    switch (rawPurpose) {
      case 'REGISTRATION':
        return 'đăng ký';
      case 'PASSWORD_RESET':
        return 'đặt lại mật khẩu';
      default:
        return rawPurpose ?? 'xác thực';
    }
  }

  private requiredString(data: EmailTemplateData, key: string, ...fallbackKeys: string[]): string {
    const value = this.optionalString(data, key, ...fallbackKeys);
    if (!value) {
      throw new Error(`EMAIL_TEMPLATE_MISSING_${key.toUpperCase()}`);
    }

    return value;
  }

  private optionalString(
    data: EmailTemplateData,
    key: string,
    ...fallbackKeys: string[]
  ): string | null {
    for (const candidate of [key, ...fallbackKeys]) {
      const value = data[candidate];
      if (value !== null && value !== undefined) {
        return typeof value === 'string' ? value : String(value);
      }
    }

    return null;
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
