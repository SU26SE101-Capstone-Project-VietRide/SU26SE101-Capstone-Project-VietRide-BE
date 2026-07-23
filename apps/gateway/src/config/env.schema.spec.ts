import { envSchema } from './env.schema';

const base = { INTERNAL_JWT_SECRET: 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa' };

describe('envSchema deep-link vars', () => {
  it('defaults DEEPLINK_APP_SCHEME to vietride and leaves the rest undefined', () => {
    const env = envSchema.parse(base);
    expect(env.DEEPLINK_APP_SCHEME).toBe('vietride');
    expect(env.DEEPLINK_ANDROID_PACKAGE).toBeUndefined();
    expect(env.DEEPLINK_ANDROID_SHA256_FINGERPRINTS).toBeUndefined();
    expect(env.DEEPLINK_ANDROID_STORE_URL).toBeUndefined();
    expect(env.APP_DEEP_LINK).toBeUndefined();
    expect(env.ANDROID_PACKAGE).toBeUndefined();
  });

  it('treats empty strings from docker compose as unset', () => {
    const env = envSchema.parse({
      ...base,
      DEEPLINK_ANDROID_PACKAGE: '',
      DEEPLINK_ANDROID_SHA256_FINGERPRINTS: '',
      DEEPLINK_APP_SCHEME: '',
      DEEPLINK_ANDROID_STORE_URL: '',
    });
    expect(env.DEEPLINK_ANDROID_PACKAGE).toBeUndefined();
    expect(env.DEEPLINK_ANDROID_STORE_URL).toBeUndefined();
    expect(env.DEEPLINK_APP_SCHEME).toBe('vietride');
  });

  it('accepts real values', () => {
    const env = envSchema.parse({
      ...base,
      DEEPLINK_ANDROID_PACKAGE: 'online.vietride.driver',
      DEEPLINK_ANDROID_SHA256_FINGERPRINTS: 'AA:BB,CC:DD',
      DEEPLINK_ANDROID_STORE_URL:
        'https://play.google.com/store/apps/details?id=online.vietride.driver',
      APP_DEEP_LINK: 'vietride://payments/return',
      ANDROID_PACKAGE: 'com.vietride.passenger',
    });
    expect(env.DEEPLINK_ANDROID_PACKAGE).toBe('online.vietride.driver');
    expect(env.APP_DEEP_LINK).toBe('vietride://payments/return');
    expect(env.ANDROID_PACKAGE).toBe('com.vietride.passenger');
  });
});
