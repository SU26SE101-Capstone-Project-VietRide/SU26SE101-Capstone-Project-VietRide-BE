import { Injectable } from '@nestjs/common';
import type { Notification, Prisma } from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';

export interface PagedNotificationsRow {
  items: Notification[];
  totalItems: number;
}

@Injectable()
export class NotificationsRepository {
  constructor(private readonly prisma: NotificationPrismaService) {}

  async listForUser(userId: string, query: ListNotificationsQueryDto): Promise<PagedNotificationsRow> {
    const where: Prisma.NotificationWhereInput = {
      userId,
      ...(query.unreadOnly ? { readAt: null } : {}),
    };
    const orderBy: Prisma.NotificationOrderByWithRelationInput = {
      [query.sortBy]: query.sortDir,
    };
    const skip = (query.page - 1) * query.pageSize;

    const [items, totalItems] = await Promise.all([
      this.prisma.notification.findMany({
        where,
        orderBy,
        skip,
        take: query.pageSize,
      }),
      this.prisma.notification.count({ where }),
    ]);

    return { items, totalItems };
  }

  async findOwnedById(notificationId: string, userId: string): Promise<Notification | null> {
    return this.prisma.notification.findFirst({
      where: { id: notificationId, userId },
    });
  }

  async markRead(notificationId: string): Promise<Notification> {
    return this.prisma.notification.update({
      where: { id: notificationId },
      data: { readAt: new Date() },
    });
  }
}
