import { Queue } from 'bullmq';
import IORedis from 'ioredis';
import type { Env } from '../config/env.schema';
import { EmailTemplateKey } from '../generated/notification-prisma-client';
import { EmailSendQueue } from './email-send.queue';
import { FcmPushQueue } from './fcm-push.queue';

const mockAdd = jest.fn();
const mockClose = jest.fn();
const mockGetJob = jest.fn();

jest.mock('bullmq', () => ({
  Queue: jest.fn().mockImplementation(() => ({
    add: mockAdd,
    close: mockClose,
    getJob: mockGetJob,
  })),
}));

jest.mock('ioredis', () => ({
  __esModule: true,
  default: jest.fn().mockImplementation(() => ({ quit: jest.fn() })),
}));

const env = { REDIS_URL: 'redis://localhost:6379' } as Env;
const fcmData = {
  notificationId: '11111111-1111-4111-8111-111111111111',
  userId: '22222222-2222-4222-8222-222222222222',
};
const emailData = {
  emailDeliveryId: '33333333-3333-4333-8333-333333333333',
  toEmail: 'passenger@vietride.local',
  templateKey: EmailTemplateKey.AUTH_OTP,
  templateData: { code: '123456', purpose: 'REGISTRATION', ttlMinutes: 5 },
};

describe('notification BullMQ producers', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockGetJob.mockResolvedValue(undefined);
  });

  it.each([
    ['FCM', () => new FcmPushQueue(env)],
    ['email', () => new EmailSendQueue(env)],
  ])('retains completed %s jobs for deterministic replay handling', (_, createQueue) => {
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
  });

  it.each([
    ['FCM', () => new FcmPushQueue(env), fcmData],
    ['email', () => new EmailSendQueue(env), emailData],
  ])('adds an absent %s job after its database row was persisted', async (_, createQueue, data) => {
    await createQueue().enqueue(data as never);

    expect(mockAdd).toHaveBeenCalledWith(expect.any(String), data, {
      jobId: expect.any(String),
    });
  });

  it.each(['waiting', 'delayed', 'active', 'prioritized', 'waiting-children'])(
    'leaves an existing in-flight %s job unchanged',
    async (state) => {
      const existing = createExistingJob(state);
      mockGetJob.mockResolvedValue(existing);

      await new FcmPushQueue(env).enqueue(fcmData);

      expect(existing.retry).not.toHaveBeenCalled();
      expect(mockAdd).not.toHaveBeenCalled();
    },
  );

  it('configures email jobs to use the worker custom backoff strategy', () => {
    new EmailSendQueue(env);

    expect(Queue).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({
        defaultJobOptions: expect.objectContaining({
          backoff: { type: 'custom', delay: 0 },
        }),
      }),
    );
  });

  it('leaves a completed FCM job unchanged to avoid duplicate push delivery', async () => {
    const existing = createExistingJob('completed');
    mockGetJob.mockResolvedValue(existing);

    await new FcmPushQueue(env).enqueue(fcmData);

    expect(existing.retry).not.toHaveBeenCalled();
    expect(mockAdd).not.toHaveBeenCalled();
  });

  it('replaces a failed FCM job so non-terminal delivery rows can recover', async () => {
    const existing = createExistingJob('failed');
    mockGetJob.mockResolvedValue(existing);

    await new FcmPushQueue(env).enqueue(fcmData);

    expect(existing.retry).toHaveBeenCalledWith('failed', { resetAttemptsMade: true });
    expect(mockAdd).not.toHaveBeenCalled();
  });

  it.each(['failed', 'completed'])(
    'replaces a terminal %s email job when the database delivery is still non-terminal',
    async (state) => {
      const existing = createExistingJob(state);
      mockGetJob.mockResolvedValue(existing);

      await new EmailSendQueue(env).enqueue(emailData);

      expect(existing.retry).toHaveBeenCalledWith(state, { resetAttemptsMade: true });
      expect(mockAdd).not.toHaveBeenCalled();
    },
  );

  it.each([
    ['FCM', () => new FcmPushQueue(env), fcmData],
    ['email', () => new EmailSendQueue(env), emailData],
  ])('fails closed for an unknown %s job state', async (_, createQueue, data) => {
    mockGetJob.mockResolvedValue(createExistingJob('unknown'));

    await expect(createQueue().enqueue(data as never)).rejects.toThrow(
      'NOTIFICATION_QUEUE_JOB_STATE_UNKNOWN',
    );
    expect(mockAdd).not.toHaveBeenCalled();
  });
});

function createExistingJob(state: string) {
  return {
    getState: jest.fn(async () => state),
    retry: jest.fn(),
  };
}
