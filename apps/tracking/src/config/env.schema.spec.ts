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
  it('boots with Local routing and an empty Goong key', () => {
    expect(loadEnv({ ...BASE_ENV, ROUTING_PROVIDER: 'LOCAL', GOONG_API_KEY: '' })).toEqual(
      expect.objectContaining({ ROUTING_PROVIDER: 'LOCAL', GOONG_API_KEY: '' }),
    );
  });

  it('fails startup when Goong routing is selected without a key', () => {
    expect(() => loadEnv({ ...BASE_ENV, ROUTING_PROVIDER: 'GOONG', GOONG_API_KEY: '  ' })).toThrow(
      'GOONG_API_KEY is required',
    );
  });

  it('accepts Goong routing with a nonblank key and applies routing defaults', () => {
    expect(loadEnv({ ...BASE_ENV, ROUTING_PROVIDER: 'GOONG', GOONG_API_KEY: 'fake-key' })).toEqual(
      expect.objectContaining({
        ROUTING_PROVIDER: 'GOONG',
        GOONG_BASE_URL: 'https://rsapi.goong.io',
        GOONG_MAX_DESTINATIONS_PER_REQUEST: 10,
        TRACKING_ROUTING_TIMEOUT_MS: 1_500,
      }),
    );
  });

  it('rejects an unsupported routing provider', () => {
    expect(() => loadEnv({ ...BASE_ENV, ROUTING_PROVIDER: 'UNSUPPORTED' })).toThrow();
  });

  it('rejects ETA intervals below the 60 second minimum', () => {
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_ETA_MIN_INTERVAL_SECONDS: '59' })).toThrow();
  });

  it('defaults route-stop cache freshness to 60 seconds', () => {
    expect(loadEnv(BASE_ENV).TRACKING_ROUTE_STOPS_CACHE_TTL_SECONDS).toBe(60);
  });

  it('requires a share-token secret with at least 32 characters', () => {
    const { TRACKING_SHARE_TOKEN_SECRET: shareTokenSecret, ...withoutSecret } = BASE_ENV;
    void shareTokenSecret;

    expect(() => loadEnv(withoutSecret)).toThrow();
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_SHARE_TOKEN_SECRET: 'too-short' })).toThrow(
      'TRACKING_SHARE_TOKEN_SECRET must be at least 32 characters',
    );
  });

  it('requires a valid share page URL', () => {
    expect(() => loadEnv({ ...BASE_ENV, TRACKING_SHARE_PAGE_URL: 'not-a-url' })).toThrow();
  });

  it('applies the Phase 13 sharing defaults', () => {
    expect(loadEnv(BASE_ENV)).toEqual(
      expect.objectContaining({
        TRACKING_SHARE_TOKEN_TTL_SECONDS: 86_400,
        TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN: 60,
        TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN: 20,
        TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS: 60,
      }),
    );
  });
});
