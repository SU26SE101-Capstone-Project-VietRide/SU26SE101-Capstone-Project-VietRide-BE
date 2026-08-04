import { loadEnv } from './env.schema';

const BASE_ENV = {
  NODE_ENV: 'test',
  DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
  REDIS_URL: 'redis://localhost:6379',
  RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
  TRACKING_SHARE_TOKEN_SECRET: 'phase13-test-share-token-secret-32-bytes',
  TRACKING_SHARE_PAGE_URL: 'http://localhost:5173/trip-sharing',
};

describe('Tracking Phase 10 environment', () => {
  it('boots with Google Routes disabled and an empty key', () => {
    expect(loadEnv({ ...BASE_ENV, GOOGLE_ROUTES_ENABLED: 'false', GOOGLE_ROUTES_API_KEY: '' }))
      .toEqual(expect.objectContaining({ GOOGLE_ROUTES_ENABLED: false, GOOGLE_ROUTES_API_KEY: '' }));
  });

  it('fails startup when Google Routes is enabled without a key', () => {
    expect(() => loadEnv({ ...BASE_ENV, GOOGLE_ROUTES_ENABLED: 'true', GOOGLE_ROUTES_API_KEY: '' }))
      .toThrow('GOOGLE_ROUTES_API_KEY is required');
  });

  it('rejects ETA intervals below the 60 second minimum', () => {
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_ETA_MIN_INTERVAL_SECONDS: '59' })).toThrow();
  });

  it('defaults route-stop cache freshness to 60 seconds', () => {
    expect(loadEnv(BASE_ENV).TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS).toBe(60);
  });

  it('requires a share-token secret with at least 32 characters', () => {
    const { TRACKING_SHARE_TOKEN_SECRET: _secret, ...withoutSecret } = BASE_ENV;

    expect(() => loadEnv(withoutSecret)).toThrow();
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_SHARE_TOKEN_SECRET: 'too-short' })).toThrow(
      'TRACKING_SHARE_TOKEN_SECRET must be at least 32 characters',
    );
  });

  it('requires a valid share page URL', () => {
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_SHARE_PAGE_URL: 'not-a-url' })).toThrow();
  });

  it('applies the Phase 13 sharing defaults', () => {
    expect(loadEnv(BASE_ENV)).toEqual(expect.objectContaining({
      TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400,
      TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 60,
      TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 20,
      TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 60,
    }));
  });
});
