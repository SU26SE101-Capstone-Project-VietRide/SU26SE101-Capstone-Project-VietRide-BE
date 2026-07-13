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
});
