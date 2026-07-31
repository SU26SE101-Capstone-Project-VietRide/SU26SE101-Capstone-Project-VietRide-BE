import { loadEnv } from './env.schema';

const BASE_ENV = {
  NODE_ENV: 'test',
  DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_tracking',
  REDIS_URL: 'redis://localhost:6379',
  RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
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
});
