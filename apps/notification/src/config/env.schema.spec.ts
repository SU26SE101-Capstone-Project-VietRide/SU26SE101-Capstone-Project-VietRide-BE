import { loadEnv } from './env.schema';

const requiredEnv = {
  DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/vietride_notification',
  REDIS_URL: 'redis://localhost:6379',
  RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
};

describe('notification env schema', () => {
  it.each([
    ['false', false],
    ['FALSE', false],
    ['true', true],
    ['TRUE', true],
  ] as const)('parses FCM_DRY_RUN=%s as %s', (raw, expected) => {
    expect(loadEnv({ ...requiredEnv, FCM_DRY_RUN: raw } as NodeJS.ProcessEnv).FCM_DRY_RUN).toBe(
      expected,
    );
  });

  it('rejects ambiguous FCM_DRY_RUN values', () => {
    expect(() =>
      loadEnv({ ...requiredEnv, FCM_DRY_RUN: 'not-a-boolean' } as NodeJS.ProcessEnv),
    ).toThrow();
  });

  it('defaults Notification Socket.IO CORS to wildcard outside production', () => {
    expect(loadEnv(requiredEnv as NodeJS.ProcessEnv).NOTIFICATION_CORS_ORIGIN).toBe('*');
  });

  it('requires a restricted Notification Socket.IO origin in production', () => {
    const productionEnv = {
      ...requiredEnv,
      NODE_ENV: 'production',
      SENDGRID_API_KEY: 'sendgrid-key',
      SENDGRID_FROM_EMAIL: 'noreply@vietride.local',
      FCM_PROJECT_ID: 'project-id',
      FCM_CLIENT_EMAIL: 'firebase@vietride.local',
      FCM_PRIVATE_KEY: 'private-key',
    } as NodeJS.ProcessEnv;

    expect(() => loadEnv({ ...productionEnv, NOTIFICATION_CORS_ORIGIN: '*' })).toThrow(
      'NOTIFICATION_CORS_ORIGIN must be restricted in production',
    );
    expect(() => loadEnv({
      ...productionEnv,
      NOTIFICATION_CORS_ORIGIN: 'https://app.vietride.online, *',
    })).toThrow('NOTIFICATION_CORS_ORIGIN must be restricted in production');
    expect(loadEnv({
      ...productionEnv,
      NOTIFICATION_CORS_ORIGIN: 'https://app.vietride.online,https://vietride.online',
    }).NOTIFICATION_CORS_ORIGIN).toBe(
      'https://app.vietride.online,https://vietride.online',
    );
  });
});
