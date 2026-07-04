import { envSchema } from './env.schema';

describe('env.schema', () => {
  it('defaults OPENROUTER_CHAT_MODEL to nvidia/nemotron-3-ultra-550b-a55b:free', () => {
    const env = envSchema.parse({
      DATABASE_URL: 'postgresql://user:pass@localhost:5432/rag',
      REDIS_URL: 'redis://localhost:6379',
      RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
      OPENROUTER_API_KEY: 'test-key',
      CLOUDINARY_CLOUD_NAME: 'cloud',
      CLOUDINARY_API_KEY: 'ckey',
      CLOUDINARY_API_SECRET: 'csec',
    });
    expect(env.OPENROUTER_CHAT_MODEL).toBe('nvidia/nemotron-3-ultra-550b-a55b:free');
  });

  it('parses OPENROUTER_CHAT_MODEL override', () => {
    const env = envSchema.parse({
      DATABASE_URL: 'postgresql://user:pass@localhost:5432/rag',
      REDIS_URL: 'redis://localhost:6379',
      RABBITMQ_URL: 'amqp://guest:guest@localhost:5672',
      OPENROUTER_API_KEY: 'test-key',
      OPENROUTER_CHAT_MODEL: 'custom/model:free',
      CLOUDINARY_CLOUD_NAME: 'cloud',
      CLOUDINARY_API_KEY: 'ckey',
      CLOUDINARY_API_SECRET: 'csec',
    });
    expect(env.OPENROUTER_CHAT_MODEL).toBe('custom/model:free');
  });
});
