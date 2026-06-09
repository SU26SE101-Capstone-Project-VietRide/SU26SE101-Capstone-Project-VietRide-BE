import { NotFoundException } from '@nestjs/common';
import { NotificationType, type Notification } from '../generated/notification-prisma-client';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';
import { FcmPushQueue } from './fcm-push.queue';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

const OWNER_USER_ID = '11111111-1111-4111-8111-111111111111';
const NOTIFICATION_ID = '22222222-2222-4222-8222-222222222222';

describe('NotificationsService', () => {
  let repository: jest.Mocked<NotificationsRepository>;
  let fcmPushQueue: jest.Mocked<FcmPushQueue>;
  let service: NotificationsService;

  beforeEach(() => {
    repository = {
      create: jest.fn(),
      listForUser: jest.fn(),
      findOwnedById: jest.fn(),
      markRead: jest.fn(),
    } as unknown as jest.Mocked<NotificationsRepository>;
    fcmPushQueue = {
      enqueue: jest.fn(),
    } as unknown as jest.Mocked<FcmPushQueue>;
    service = new NotificationsService(repository, fcmPushQueue);
  });

  it('creates a normalized notification DTO', async () => {
    repository.create.mockResolvedValue(
      createNotification({
        type: NotificationType.BOOKING_CONFIRMED,
        title: 'Dat ve thanh cong',
        body: 'Ve cua ban da duoc xac nhan.',
        data: { bookingId: '33333333-3333-4333-8333-333333333333' },
      }),
    );

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
    });
  });

  it('marks an owned unread notification as read', async () => {
    repository.findOwnedById.mockResolvedValue(createNotification({ readAt: null }));
    repository.markRead.mockResolvedValue(createNotification({ readAt: new Date('2026-06-01T10:01:00.000Z') }));

    await expect(service.markRead(NOTIFICATION_ID, OWNER_USER_ID)).resolves.toBeUndefined();

    expect(repository.markRead).toHaveBeenCalledWith(NOTIFICATION_ID);
  });

  it('does not update an already read notification', async () => {
    repository.findOwnedById.mockResolvedValue(createNotification({ readAt: new Date('2026-06-01T10:01:00.000Z') }));

    await service.markRead(NOTIFICATION_ID, OWNER_USER_ID);

    expect(repository.markRead).not.toHaveBeenCalled();
  });

  it('throws NOTIFICATION_NOT_FOUND when notification is not owned by user', async () => {
    repository.findOwnedById.mockResolvedValue(null);

    await expect(service.markRead(NOTIFICATION_ID, OWNER_USER_ID)).rejects.toThrow(NotFoundException);
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
    readAt: null,
    createdAt: new Date('2026-06-01T10:00:00.000Z'),
    ...overrides,
  };
}
