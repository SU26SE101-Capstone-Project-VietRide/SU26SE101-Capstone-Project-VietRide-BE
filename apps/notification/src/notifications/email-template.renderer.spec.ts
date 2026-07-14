import { EmailTemplateKey } from '../generated/notification-prisma-client';
import { EmailTemplateRenderer } from './email-template.renderer';

describe('EmailTemplateRenderer', () => {
  const renderer = new EmailTemplateRenderer();

  describe('AUTH_OTP', () => {
    it('renders the OTP from the Identity `code` field and maps REGISTRATION purpose', () => {
      const result = renderer.render(EmailTemplateKey.AUTH_OTP, {
        code: '123456',
        purpose: 'REGISTRATION',
        ttlMinutes: 5,
      });

      expect(result.subject).toBe('Ma xac thuc VietRide');
      expect(result.text).toContain('123456');
      expect(result.text).toContain('dang ky');
      expect(result.text).toContain('5 phut');
      expect(result.html).toContain('<strong>123456</strong>');
    });

    it('maps PASSWORD_RESET purpose to Vietnamese copy', () => {
      const result = renderer.render(EmailTemplateKey.AUTH_OTP, {
        code: '654321',
        purpose: 'PASSWORD_RESET',
        ttlMinutes: 5,
      });

      expect(result.text).toContain('dat lai mat khau');
    });

    it('still accepts the legacy `otpCode` field', () => {
      const result = renderer.render(EmailTemplateKey.AUTH_OTP, {
        otpCode: '999000',
        ttlMinutes: 10,
      });

      expect(result.text).toContain('999000');
    });

    it('throws when neither code nor otpCode is provided', () => {
      expect(() => renderer.render(EmailTemplateKey.AUTH_OTP, { ttlMinutes: 5 })).toThrow(
        'EMAIL_TEMPLATE_MISSING_OTPCODE',
      );
    });
  });

  describe('SET_INITIAL_PASSWORD', () => {
    it('renders the set-password link from the Identity `setInitialPasswordUrl` field', () => {
      const result = renderer.render(EmailTemplateKey.SET_INITIAL_PASSWORD, {
        userId: '11111111-1111-4111-8111-111111111111',
        displayName: 'Staff Member',
        setInitialPasswordUrl: 'https://app.vietride.app/auth/set-password?token=abc',
        expiresAt: '2026-06-23T10:00:00.000Z',
      });

      expect(result.subject).toBe('Thiet lap mat khau VietRide');
      expect(result.text).toContain('https://app.vietride.app/auth/set-password?token=abc');
      expect(result.html).toContain('href="https://app.vietride.app/auth/set-password?token=abc"');
    });

    it('still accepts the legacy `setPasswordUrl` field', () => {
      const result = renderer.render(EmailTemplateKey.SET_INITIAL_PASSWORD, {
        setPasswordUrl: 'https://app.vietride.app/auth/set-password?token=legacy',
      });

      expect(result.text).toContain('token=legacy');
    });
  });

  describe('INVOICE_NOTICE', () => {
    it('uses the Operator Web URL as the only email href', () => {
      const invoiceWebUrl =
        'https://operator.vietride.vn/invoices/77777777-7777-4777-8777-777777777777';
      const result = renderer.render(EmailTemplateKey.INVOICE_NOTICE, {
        invoiceNumber: 'VR-INV-202607-000001',
        amountVnd: '1200000',
        invoiceUrl: invoiceWebUrl,
      });

      expect(result.html).toContain(`href="${invoiceWebUrl}"`);
      expect(result.text).toContain(invoiceWebUrl);
      expect(result.html).not.toContain('/download');
    });
  });
});
