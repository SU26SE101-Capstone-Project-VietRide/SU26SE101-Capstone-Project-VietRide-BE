import { Injectable, NotFoundException } from '@nestjs/common';
import type { Notification } from '../generated/notification-prisma-client';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';
import { NotificationsRepository } from './notifications.repository';

export interface NotificationItemDto {
  id: string;
  userId: string;
  type: string;
  title: string;
  body: string;
  data: unknown;
  readAt: string | null;
  createdAt: string;
}

export interface PagedNotificationsDto {
  items: NotificationItemDto[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

@Injectable()
export class NotificationsService {
  constructor(private readonly notificationsRepository: NotificationsRepository) {}

  async listForUser(userId: string, query: ListNotificationsQueryDto): Promise<PagedNotificationsDto> {
    const result = await this.notificationsRepository.listForUser(userId, query);
    const totalPages = Math.ceil(result.totalItems / query.pageSize);

    return {
      items: result.items.map((notification) => this.toDto(notification)),
      page: query.page,
      pageSize: query.pageSize,
      totalItems: result.totalItems,
      totalPages,
      hasNextPage: query.page < totalPages,
      hasPreviousPage: query.page > 1,
    };
  }

  async markRead(notificationId: string, userId: string): Promise<void> {
    const notification = await this.notificationsRepository.findOwnedById(notificationId, userId);
    if (!notification) {
      throw new NotFoundException({
        errorCode: 'NOTIFICATION_NOT_FOUND',
        detail: `Notification ${notificationId} not found`,
      });
    }

    if (!notification.readAt) {
      await this.notificationsRepository.markRead(notificationId);
    }
  }

  private toDto(notification: Notification): NotificationItemDto {
    return {
      id: notification.id,
      userId: notification.userId,
      type: notification.type,
      title: notification.title,
      body: notification.body,
      data: notification.data ?? null,
      readAt: notification.readAt ? notification.readAt.toISOString() : null,
      createdAt: notification.createdAt.toISOString(),
    };
  }
}
