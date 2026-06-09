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

  it('truncates long messages', () => {
    expect(normalizeSafeError(new Error('abcdefghij'), 4)).toBe('abcd');
  });
});
