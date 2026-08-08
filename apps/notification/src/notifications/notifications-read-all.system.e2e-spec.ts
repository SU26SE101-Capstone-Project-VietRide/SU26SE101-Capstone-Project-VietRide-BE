import { randomUUID } from 'node:crypto';
import IORedis from 'ioredis';
import { RedisService } from '@vietride/nest-redis';
import { NotificationType } from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import type { EmailSendQueue } from './email-send.queue';
import { EmailTemplateRenderer } from './email-template.renderer';
import type { FcmPushQueue } from './fcm-push.queue';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

const describeSystem =
  process.env.NOTIFICATION_READ_ALL_SYSTEM_E2E === '1' ? describe : describe.skip;

describeSystem('Notification read-all and cursor (real PostgreSQL + Redis)', () => {
  const userIds: string[] = [];
  const redisKeys: string[] = [];
  let redisClient: IORedis;
  let redis: RedisService;
  let prisma: NotificationPrismaService;
  let service: NotificationsService;

  beforeAll(async () => {
    redisClient = new IORedis(process.env.REDIS_URL as string, { maxRetriesPerRequest: null });
    redis = new RedisService(redisClient);
    prisma = new NotificationPrismaService();
    await prisma.$connect();
    service = new NotificationsService(
      new NotificationsRepository(prisma),
      {} as FcmPushQueue,
      {} as EmailSendQueue,
      new EmailTemplateRenderer(),
      redis,
    );
  });

  afterAll(async () => {
    await prisma.notification.deleteMany({ where: { userId: { in: userIds } } });
    if (redisKeys.length > 0) await redisClient.del(...redisKeys);
    await prisma.$disconnect();
    await redisClient.quit();
  });

  it('persists one cutoff before mutation and leaves post-cutoff inserts unread on retry', async () => {
    const userId = randomUUID();
    const idempotencyKey = randomUUID();
    userIds.push(userId);
    redisKeys.push(`notification:read-all:${userId}:${idempotencyKey}`);
    await seedNotification(userId, new Date(Date.now() - 2_000));
    await seedNotification(userId, new Date(Date.now() - 1_000));

    const first = await service.markAllRead(userId, idempotencyKey);
    expect(first.markedCount).toBe(2);

    const afterCutoff = await seedNotification(
      userId,
      new Date(new Date(first.readAt).getTime() + 1_000),
    );
    const retry = await service.markAllRead(userId, idempotencyKey);
    expect(retry).toEqual({ markedCount: 0, readAt: first.readAt });

    const rows = await prisma.notification.findMany({
      where: { userId },
      orderBy: { createdAt: 'asc' },
    });
    expect(rows.slice(0, 2).every((row) => row.readAt?.toISOString() === first.readAt)).toBe(true);
    expect(rows.find((row) => row.id === afterCutoff.id)?.readAt).toBeNull();
    await expect(redisClient.ttl(redisKeys.at(-1) as string)).resolves.toBeGreaterThan(0);
  });

  it('uses a real snapshot keyset so an interleaved insert is neither duplicated nor leaked', async () => {
    const userId = randomUUID();
    userIds.push(userId);
    const seeded = await Promise.all([
      seedNotification(userId, new Date(Date.now() - 4_000)),
      seedNotification(userId, new Date(Date.now() - 3_000)),
      seedNotification(userId, new Date(Date.now() - 2_000)),
      seedNotification(userId, new Date(Date.now() - 1_000)),
    ]);
    const query = {
      unreadOnly: false,
      page: 1,
      pageSize: 2,
      sortBy: 'createdAt' as const,
      sortDir: 'desc' as const,
    };

    const first = await service.listForUser(userId, query);
    expect(first.nextCursor).toEqual(expect.any(String));
    await seedNotification(userId, new Date(Date.now() + 1_000));
    const second = await service.listForUser(userId, {
      ...query,
      page: 2,
      cursor: first.nextCursor as string,
    });

    const pagedIds = [...first.items, ...second.items].map((item) => item.id);
    expect(new Set(pagedIds)).toEqual(new Set(seeded.map((item) => item.id)));
    expect(pagedIds).toHaveLength(4);
  });

  async function seedNotification(userId: string, createdAt: Date) {
    return prisma.notification.create({
      data: {
        userId,
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'System E2E',
        body: 'Notification read-all/cursor verification',
        createdAt,
      },
    });
  }
});
