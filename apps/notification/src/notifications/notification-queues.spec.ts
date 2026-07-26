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
      const calls = (Queue as unknown as jest.Mock).mock.calls as unknown[][];
      const options = calls[0]?.[1] as
        | { defaultJobOptions?: { removeOnComplete?: unknown } }
        | undefined;
      expect(options?.defaultJobOptions?.removeOnComplete).toEqual({
        age: 86_400,
        count: 10_000,
      });
    },
  );

  it('uses notificationId as deterministic FCM jobId so replay produces one effective queue job', async () => {
    const queue = new FcmPushQueue(env);
    const add = (Queue as unknown as jest.Mock).mock.results[0]?.value.add as jest.Mock;
    const notification = {
      notificationId: '11111111-1111-4111-8111-111111111111',
      userId: '22222222-2222-4222-8222-222222222222',
    };

    await queue.enqueue(notification);
    await queue.enqueue(notification);

    expect(add).toHaveBeenCalledTimes(2);
    expect(add).toHaveBeenNthCalledWith(1, expect.any(String), notification, {
      jobId: notification.notificationId,
    });
    expect(add).toHaveBeenNthCalledWith(2, expect.any(String), notification, {
      jobId: notification.notificationId,
    });
    expect(new Set(add.mock.calls.map((call) => call[2]?.jobId))).toEqual(
      new Set([notification.notificationId]),
    );
  });
});
