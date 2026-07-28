import { normalizeSafeError } from './safe-error';

describe('normalizeSafeError', () => {
  it('redacts bearer tokens and sensitive key values', () => {
    expect(
      normalizeSafeError(
        new Error('provider failed Bearer abc.def.ghi otpCode=123456 deliveryToken=super-secret'),
        200,
      ),
    ).toBe('provider failed Bearer [REDACTED] otpCode=[REDACTED] deliveryToken=[REDACTED]');
  });

  it('redacts standalone JWTs, emails, FCM tokens and signed URLs', () => {
    const jwt = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.signature-secret';
    const signedUrl =
      'https://storage.example/invoice.pdf?X-Goog-Algorithm=GOOG4-RSA-SHA256&X-Goog-Signature=signed-secret';

    const output = normalizeSafeError(
      new Error(
        `notify admin@vietride.local jwt=${jwt} fcmToken=fcm-device-token-secret url=${signedUrl}`,
      ),
      1_000,
    );

    expect(output).not.toContain('admin@vietride.local');
    expect(output).not.toContain(jwt);
    expect(output).not.toContain('fcm-device-token-secret');
    expect(output).not.toContain('X-Goog-Signature');
    expect(output).not.toContain('signed-secret');
  });

  it('truncates long messages', () => {
    expect(normalizeSafeError(new Error('abcdefghij'), 4)).toBe('abcd');
  });
});
