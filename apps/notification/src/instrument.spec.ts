jest.mock('./bootstrap-env', () => ({}));

describe('notification Sentry instrumentation', () => {
  const SENTRY_DSN = 'https://key@o0.ingest.us.sentry.io/0';
  const originalSentryDsn = process.env.SENTRY_DSN;
  const originalNodeEnv = process.env.NODE_ENV;

  beforeEach(() => {
    jest.resetModules();
    jest.restoreAllMocks();
  });

  afterEach(() => {
    restoreEnv('SENTRY_DSN', originalSentryDsn);
    restoreEnv('NODE_ENV', originalNodeEnv);
  });

  it('does not initialize Sentry without a DSN', () => {
    delete process.env.SENTRY_DSN;
    const sentry = require('@sentry/nestjs');
    const initSpy = jest.spyOn(sentry, 'init');

    require('./instrument');

    expect(initSpy).not.toHaveBeenCalled();
  });

  it('initializes without default PII and scrubs the complete event before send', () => {
    process.env.SENTRY_DSN = SENTRY_DSN;
    process.env.NODE_ENV = 'test';
    const sentry = require('@sentry/nestjs');
    const initSpy = jest.spyOn(sentry, 'init');

    require('./instrument');

    expect(initSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        dsn: SENTRY_DSN,
        environment: 'test',
        sendDefaultPii: false,
        tracesSampleRate: 0,
        beforeSend: expect.any(Function),
      }),
    );

    const options = initSpy.mock.calls[0]?.[0] as {
      beforeSend: (event: Record<string, unknown>) => Record<string, unknown>;
    };
    const jwt = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.signature-secret';
    const scrubbed = options.beforeSend({
      message: `failed for admin@vietride.local Bearer ${jwt}`,
      request: {
        headers: { authorization: `Bearer ${jwt}` },
        data: {
          passengerName: 'Nguyen Van Sensitive',
          payload: { fcmToken: 'fcm-device-token-secret', otpCode: '123456' },
        },
        url: 'https://storage.example/invoice.pdf?X-Goog-Signature=signed-secret',
      },
      extra: { rawPayload: { email: 'nested@vietride.local' } },
    });
    const serialized = JSON.stringify(scrubbed);

    expect(serialized).toContain('[REDACTED]');
    expect(serialized).not.toContain('admin@vietride.local');
    expect(serialized).not.toContain('nested@vietride.local');
    expect(serialized).not.toContain(jwt);
    expect(serialized).not.toContain('fcm-device-token-secret');
    expect(serialized).not.toContain('123456');
    expect(serialized).not.toContain('Nguyen Van Sensitive');
    expect(serialized).not.toContain('X-Goog-Signature');
    expect(serialized).not.toContain('signed-secret');
  });
});

function restoreEnv(key: 'SENTRY_DSN' | 'NODE_ENV', value: string | undefined): void {
  if (value === undefined) delete process.env[key];
  else process.env[key] = value;
}
