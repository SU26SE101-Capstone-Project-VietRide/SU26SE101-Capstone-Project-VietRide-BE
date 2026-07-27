import { PassThrough } from 'node:stream';
import { createNotificationLogger } from './notification-logger';

describe('createNotificationLogger', () => {
  it('redacts nested secrets and sensitive values embedded in log messages', async () => {
    const stream = new PassThrough();
    let output = '';
    stream.on('data', (chunk: Buffer) => {
      output += chunk.toString('utf8');
    });
    const logger = createNotificationLogger('redaction-test', stream);

    logger.info(
      {
        email: 'admin@vietride.local',
        payload: { secret: 'full-event-payload' },
        error: 'provider failed for admin@vietride.local',
        downloadApiUrl: 'https://api.vietride.vn/v1/operator/invoices/id/download',
        signedUrl: 'https://storage.example/invoice.pdf?X-Goog-Signature=secret',
        nested: {
          auth: { fcmToken: 'fcm-device-token-secret' },
          contact: { email: 'nested@vietride.local' },
          request: { payload: { otpCode: '123456' } },
        },
        emailDeliveryId: '44444444-4444-4444-8444-444444444444',
        issues: [{ code: 'invalid_type' }],
      },
      'Bearer abc.def.ghi failed for message@vietride.local at https://storage.example/invoice.pdf?X-Goog-Signature=message-secret',
    );
    await new Promise<void>((resolve) => stream.end(resolve));

    expect(output).toContain('[REDACTED]');
    expect(output).not.toContain('admin@vietride.local');
    expect(output).not.toContain('full-event-payload');
    expect(output).not.toContain('/download');
    expect(output).not.toContain('X-Goog-Signature');
    expect(output).not.toContain('fcm-device-token-secret');
    expect(output).not.toContain('nested@vietride.local');
    expect(output).not.toContain('123456');
    expect(output).not.toContain('abc.def.ghi');
    expect(output).not.toContain('message@vietride.local');
    expect(output).not.toContain('message-secret');
    expect(output).toContain('44444444-4444-4444-8444-444444444444');
    expect(output).toContain('invalid_type');
  });
});
