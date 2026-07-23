import { Queue } from 'bullmq';
import IORedis from 'ioredis';
import type { Env } from '../config/env.schema';
import { EmailSendQueue } from './email-send.queue';
import { FcmPushQueue } from './fcm-push.queue';

jest.mock('bullmq', () => ({
  Queue: jest.fn().mockImplementation(() => ({
    add: jest.fn(),
    close: jest.fn(),
  })),
}));

jest.mock('ioredis', () => ({
  __esModule: true,
  default: jest.fn().mockImplementation(() => ({ quit: jest.fn() })),
}));

const env = { REDIS_URL: 'redis://localhost:6379' } as Env;

describe('notification BullMQ producer retention', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it.each([
    ['FCM', () => new FcmPushQueue(env)],
    ['email', () => new EmailSendQueue(env)],
  ])(
    'retains completed %s jobs long enough for deterministic job IDs to deduplicate replay',
    (_, createQueue) => {
      createQueue();

      expect(IORedis).toHaveBeenCalledWith(env.REDIS_URL, { maxRetriesPerRequest: null });
      expect(Queue).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          defaultJobOptions: expect.objectContaining({
            removeOnComplete: {
              age: 86_400,
              count: 10_000,
            },
          }),
        }),
      );
    },
  );
});
