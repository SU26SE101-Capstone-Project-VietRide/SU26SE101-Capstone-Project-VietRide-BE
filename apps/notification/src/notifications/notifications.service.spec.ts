import { BadRequestException, NotFoundException } from '@nestjs/common';
import type { RedisService } from '@vietride/nest-redis';
import {
  EmailDeliveryStatus,
  EmailTemplateKey,
  NotificationType,
  type EmailDelivery,
  type Notification,
} from '../generated/notification-prisma-client';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';
import { EmailSendQueue } from './email-send.queue';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

const OWNER_USER_ID = '11111111-1111-4111-8111-111111111111';
const NOTIFICATION_ID = '22222222-2222-4222-8222-222222222222';

describe('NotificationsService', () => {
  let repository: jest.Mocked<NotificationsRepository>;
  let fcmPushQueue: jest.Mocked<FcmPushQueue>;
  let emailSendQueue: jest.Mocked<EmailSendQueue>;
  let redis: jest.Mocked<RedisService>;
  let redisClient: { set: jest.Mock };
  let service: NotificationsService;

  beforeEach(() => {
    repository = {
      create: jest.fn(),
      listForUser: jest.fn(),
      findOwnedById: jest.fn(),
      markRead: jest.fn(),
      markAllRead: jest.fn(),
      createEmailDelivery: jest.fn(),
    } as unknown as jest.Mocked<NotificationsRepository>;
    fcmPushQueue = {
      enqueue: jest.fn(),
    } as unknown as jest.Mocked<FcmPushQueue>;
    emailSendQueue = {
      enqueue: jest.fn(),
    } as unknown as jest.Mocked<EmailSendQueue>;
    redisClient = { set: jest.fn() };
    redis = {
      getClient: jest.fn().mockReturnValue(redisClient),
      get: jest.fn(),
    } as unknown as jest.Mocked<RedisService>;
    service = new NotificationsService(
      repository,
      fcmPushQueue,
      emailSendQueue,
      new EmailTemplateRenderer(),
      redis,
    );
  });

  it('creates a normalized notification DTO', async () => {
    repository.create.mockResolvedValue({
      notification: createNotification({
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'Dat ve thanh cong',
        body: 'Ve cua ban da duoc xac nhan.',
        data: { bookingId: '33333333-3333-4333-8333-333333333333' },
      }),
      created: true,
    });

    await expect(
      service.createNotification({
        userId: OWNER_USER_ID,
        type: NotificationType.BOOKING_CONFIRMED,
        title: '  Dat ve thanh cong  ',
        body: '  Ve cua ban da duoc xac nhan.  ',
        data: { bookingId: '33333333-3333-4333-8333-333333333333' },
      }),
    ).resolves.toEqual(
      expect.objectContaining({
        id: NOTIFICATION_ID,
        userId: OWNER_USER_ID,
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'Dat ve thanh cong',
        body: 'Ve cua ban da duoc xac nhan.',
        data: { bookingId: '33333333-3333-4333-8333-333333333333' },
      }),
    );
    expect(repository.create).toHaveBeenCalledWith({
      userId: OWNER_USER_ID,
      type: NotificationType.BOOKING_CONFIRMED,
      title: 'Dat ve thanh cong',
      body: 'Ve cua ban da duoc xac nhan.',
      data: { bookingId: '33333333-3333-4333-8333-333333333333' },
    });
    expect(fcmPushQueue.enqueue).toHaveBeenCalledWith({
      notificationId: NOTIFICATION_ID,
      userId: OWNER_USER_ID,
    });
  });

  it('re-enqueues any existing notification so a failed first queue write can recover', async () => {
    repository.create.mockResolvedValue({
      notification: createNotification({ type: NotificationType.SHUTTLE_ASSIGNED }),
      created: false,
    });

    await service.createNotification({
      userId: OWNER_USER_ID,
      type: NotificationType.SHUTTLE_ASSIGNED,
      title: 'Shuttle assigned',
      body: 'Driver is on the way.',
      dedupeKey: 'trip.shuttle.assigned:booking-id',
    });

    expect(fcmPushQueue.enqueue).toHaveBeenCalledWith({
      notificationId: NOTIFICATION_ID,
      userId: OWNER_USER_ID,
    });
  });

  it('persisted VEHICLE_SUBSTITUTED row survives enqueue failure and redelivery re-enqueues the same deduped notification without creating a second row', async () => {
    const persisted = createNotification({ type: NotificationType.VEHICLE_SUBSTITUTED });
    repository.create
      .mockResolvedValueOnce({ notification: persisted, created: true })
      .mockResolvedValueOnce({ notification: persisted, created: false });
    fcmPushQueue.enqueue.mockRejectedValueOnce(new Error('Redis unavailable'));
    const dto = {
      userId: OWNER_USER_ID,
      type: NotificationType.VEHICLE_SUBSTITUTED,
      title: 'Xe thay thế đã được sắp xếp',
      body: 'Xe 51B-123.45 đã được sắp xếp.',
      dedupeKey: 'booking.booking.transferred:event:user:VEHICLE_SUBSTITUTED',
    };

    await expect(service.createNotification(dto)).rejects.toThrow('Redis unavailable');
    await expect(service.createNotification(dto)).resolves.toEqual(
      expect.objectContaining({ id: NOTIFICATION_ID }),
    );

    expect(repository.create).toHaveBeenCalledTimes(2);
    expect(fcmPushQueue.enqueue).toHaveBeenCalledTimes(2);
    expect(fcmPushQueue.enqueue).toHaveBeenNthCalledWith(2, {
      notificationId: NOTIFICATION_ID,
      userId: OWNER_USER_ID,
    });
  });

  it('returns a paged notification history DTO', async () => {
    const query: ListNotificationsQueryDto = {
      unreadOnly: false,
      page: 1,
      pageSize: 20,
      sortBy: 'createdAt',
      sortDir: 'desc',
    };
    repository.listForUser.mockResolvedValue({
      totalItems: 1,
      items: [createNotification({ readAt: null })],
    });

    await expect(service.listForUser(OWNER_USER_ID, query)).resolves.toEqual({
      items: [
        expect.objectContaining({
          id: NOTIFICATION_ID,
          userId: OWNER_USER_ID,
          readAt: null,
          createdAt: '2026-06-01T10:00:00.000Z',
        }),
      ],
      page: 1,
      pageSize: 20,
      totalItems: 1,
      totalPages: 1,
      hasNextPage: false,
      hasPreviousPage: false,
      nextCursor: null,
    });
  });

  it('uses an opaque cursor to continue the same notification snapshot', async () => {
    const firstItem = createNotification({
      id: '33333333-3333-4333-8333-333333333333',
      createdAt: new Date('2026-06-01T10:02:00.000Z'),
    });
    repository.listForUser.mockResolvedValueOnce({
      totalItems: 2,
      items: [firstItem],
      hasMore: true,
    });

    const firstPage = await service.listForUser(OWNER_USER_ID, {
      unreadOnly: true,
      page: 1,
      pageSize: 1,
      sortBy: 'createdAt',
      sortDir: 'desc',
    });

    expect(firstPage.nextCursor).toEqual(expect.any(String));
    const nextCursor = firstPage.nextCursor;
    if (!nextCursor) throw new Error('Expected a continuation cursor');
    repository.listForUser.mockResolvedValueOnce({
      totalItems: 2,
      items: [createNotification({ createdAt: new Date('2026-06-01T10:01:00.000Z') })],
      hasMore: false,
    });

    const secondPage = await service.listForUser(OWNER_USER_ID, {
      unreadOnly: false,
      page: 99,
      pageSize: 100,
      sortBy: 'type',
      sortDir: 'asc',
      cursor: nextCursor,
    });

    expect(secondPage).toMatchObject({ page: 2, pageSize: 1, nextCursor: null });
    const secondRepositoryCall = repository.listForUser.mock.calls[1];
    if (!secondRepositoryCall) throw new Error('Expected the repository to receive page two');
    const [, effectiveQuery, boundary] = secondRepositoryCall;
    expect(effectiveQuery).toMatchObject({ unreadOnly: true, page: 2, pageSize: 1 });
    expect(boundary).toMatchObject({
      lastCreatedAt: firstItem.createdAt,
      lastId: firstItem.id,
    });
  });

  it('rejects a malformed notification cursor', async () => {
    const act = service.listForUser(OWNER_USER_ID, {
        unreadOnly: false,
        page: 1,
        pageSize: 20,
        sortBy: 'createdAt',
        sortDir: 'desc',
        cursor: 'not-a-valid-cursor',
      });

    await expect(act).rejects.toBeInstanceOf(BadRequestException);
    await expect(act).rejects.toMatchObject({
      response: {
        errorCode: 'VALIDATION_FAILED',
        detail: 'Notification cursor is invalid',
      },
    });
    expect(repository.listForUser).not.toHaveBeenCalled();
  });

  it('persists a read-all cutoff before atomically updating unread rows', async () => {
    redisClient.set.mockResolvedValue('OK');
    repository.markAllRead.mockResolvedValue(3);

    const result = await service.markAllRead(
      OWNER_USER_ID,
      '55555555-5555-4555-8555-555555555555',
    );

    expect(redisClient.set).toHaveBeenCalledWith(
      `notification:read-all:${OWNER_USER_ID}:55555555-5555-4555-8555-555555555555`,
      result.readAt,
      'EX',
      86_400,
      'NX',
    );
    expect(repository.markAllRead).toHaveBeenCalledWith(OWNER_USER_ID, new Date(result.readAt));
    expect(result.markedCount).toBe(3);
  });

  it('reuses the original read-all cutoff when the idempotency key is retried', async () => {
    const originalCutoff = '2026-06-01T10:03:00.000Z';
    redisClient.set.mockResolvedValue(null);
    redis.get.mockResolvedValue(originalCutoff);
    repository.markAllRead.mockResolvedValue(0);

    await expect(
      service.markAllRead(OWNER_USER_ID, '66666666-6666-4666-8666-666666666666'),
    ).resolves.toEqual({ markedCount: 0, readAt: originalCutoff });
    expect(repository.markAllRead).toHaveBeenCalledWith(OWNER_USER_ID, new Date(originalCutoff));
  });

  it('fails closed when the read-all cutoff cannot be persisted', async () => {
    redisClient.set.mockRejectedValue(new Error('Redis unavailable'));

    await expect(
      service.markAllRead(OWNER_USER_ID, '77777777-7777-4777-8777-777777777777'),
    ).rejects.toThrow('Redis unavailable');
    expect(repository.markAllRead).not.toHaveBeenCalled();
  });

  it('marks an owned unread notification as read', async () => {
    repository.findOwnedById.mockResolvedValue(createNotification({ readAt: null }));
    repository.markRead.mockResolvedValue(
      createNotification({ readAt: new Date('2026-06-01T10:01:00.000Z') }),
    );

    await expect(service.markRead(NOTIFICATION_ID, OWNER_USER_ID)).resolves.toBeUndefined();

    expect(repository.markRead).toHaveBeenCalledWith(NOTIFICATION_ID);
  });

  it('does not update an already read notification', async () => {
    repository.findOwnedById.mockResolvedValue(
      createNotification({ readAt: new Date('2026-06-01T10:01:00.000Z') }),
    );

    await service.markRead(NOTIFICATION_ID, OWNER_USER_ID);

    expect(repository.markRead).not.toHaveBeenCalled();
  });

  it('throws NOTIFICATION_NOT_FOUND when notification is not owned by user', async () => {
    repository.findOwnedById.mockResolvedValue(null);

    await expect(service.markRead(NOTIFICATION_ID, OWNER_USER_ID)).rejects.toThrow(
      NotFoundException,
    );
  });

  it('creates sanitized email delivery audit and enqueues sensitive template data for SendGrid', async () => {
    repository.createEmailDelivery.mockResolvedValue({
      delivery: createEmailDelivery(),
      created: true,
    });

    await expect(
      service.enqueueEmail({
        toEmail: 'passenger@vietride.local',
        templateKey: EmailTemplateKey.AUTH_OTP,
        templateData: {
          otpCode: '123456',
          purpose: 'dang ky',
          ttlMinutes: 10,
        },
      }),
    ).resolves.toEqual(
      expect.objectContaining({
        id: '44444444-4444-4444-8444-444444444444',
        toEmail: 'passenger@vietride.local',
        templateKey: EmailTemplateKey.AUTH_OTP,
        status: EmailDeliveryStatus.PENDING,
      }),
    );

    const persistedEmail = repository.createEmailDelivery.mock.calls[0]?.[0];
    expect(persistedEmail).toMatchObject({
      toEmail: 'passenger@vietride.local',
      templateKey: EmailTemplateKey.AUTH_OTP,
      subject: 'Mã xác thực VietRide',
    });
    expect(persistedEmail?.sanitizedData).toEqual({
      otpCode: '[REDACTED]',
      purpose: 'dang ky',
      ttlMinutes: 10,
    });
    const queuedEmail = emailSendQueue.enqueue.mock.calls[0]?.[0];
    expect(queuedEmail).toMatchObject({
      emailDeliveryId: '44444444-4444-4444-8444-444444444444',
      toEmail: 'passenger@vietride.local',
      templateKey: EmailTemplateKey.AUTH_OTP,
    });
    expect(queuedEmail?.templateData).toMatchObject({ otpCode: '123456' });
  });

  it('re-enqueues a pending email delivery so a failed first queue write can recover', async () => {
    repository.createEmailDelivery.mockResolvedValue({
      delivery: createEmailDelivery(),
      created: false,
    });

    await service.enqueueEmail({
      dedupeKey: 'payment.invoice.issued:message:user:email',
      toEmail: 'passenger@vietride.local',
      templateKey: EmailTemplateKey.INVOICE_NOTICE,
      templateData: {
        invoiceNumber: 'VR-INV-202607-000001',
        invoiceUrl: 'https://operator.vietride.vn/invoices/one',
      },
    });

    expect(emailSendQueue.enqueue).toHaveBeenCalledWith(
      expect.objectContaining({
        emailDeliveryId: '44444444-4444-4444-8444-444444444444',
      }),
    );
  });

  it('does not enqueue an already sent email delivery on event replay', async () => {
    repository.createEmailDelivery.mockResolvedValue({
      delivery: createEmailDelivery({ status: EmailDeliveryStatus.SENT }),
      created: false,
    });

    await service.enqueueEmail({
      dedupeKey: 'payment.invoice.issued:message:user:email',
      toEmail: 'passenger@vietride.local',
      templateKey: EmailTemplateKey.INVOICE_NOTICE,
      templateData: { invoiceNumber: 'VR-INV-202607-000001' },
    });

    expect(emailSendQueue.enqueue).not.toHaveBeenCalled();
  });

  it('re-enqueues an uncertain SENDING email so its lease can be reclaimed', async () => {
    repository.createEmailDelivery.mockResolvedValue({
      delivery: createEmailDelivery({ status: EmailDeliveryStatus.SENDING }),
      created: false,
    });

    await service.enqueueEmail({
      dedupeKey: 'identity.otp.requested:message:email',
      toEmail: 'passenger@vietride.local',
      templateKey: EmailTemplateKey.AUTH_OTP,
      templateData: { code: '123456', purpose: 'REGISTRATION', ttlMinutes: 5 },
    });

    expect(emailSendQueue.enqueue).toHaveBeenCalledWith(
      expect.objectContaining({
        emailDeliveryId: '44444444-4444-4444-8444-444444444444',
      }),
    );
  });
});

function createNotification(overrides: Partial<Notification>): Notification {
  return {
    id: NOTIFICATION_ID,
    userId: OWNER_USER_ID,
    type: NotificationType.BOOKING_CONFIRMED,
    title: 'Dat ve thanh cong',
    body: 'Ve cua ban da duoc xac nhan.',
    data: { bookingId: '33333333-3333-4333-8333-333333333333' },
    dedupeKey: null,
    readAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    ...overrides,
  };
}

function createEmailDelivery(overrides: Partial<EmailDelivery> = {}): EmailDelivery {
  return {
    id: '44444444-4444-4444-8444-444444444444',
    notificationId: null,
    dedupeKey: null,
    toEmail: 'passenger@vietride.local',
    templateKey: EmailTemplateKey.AUTH_OTP,
    subject: 'Mã xác thực VietRide',
    sanitizedData: { otpCode: '[REDACTED]' },
    status: EmailDeliveryStatus.PENDING,
    retryCount: 0,
    lastError: null,
    providerMessageId: null,
    sentAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    updatedAt: new Date('2026-06-01T10:00:00.000Z'),
    ...overrides,
  };
}
