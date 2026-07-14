import { PassThrough } from 'node:stream';
import { createNotificationLogger } from './notification-logger';

describe('createNotificationLogger', () => {
  it('redacts PII, full payloads, protected download URLs and signed URLs at logger level', async () => {
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
      },
      'redaction probe',
    );
    await new Promise<void>((resolve) => stream.end(resolve));

    expect(output).toContain('[REDACTED]');
    expect(output).not.toContain('admin@vietride.local');
    expect(output).not.toContain('full-event-payload');
    expect(output).not.toContain('/download');
    expect(output).not.toContain('X-Goog-Signature');
  });
});
