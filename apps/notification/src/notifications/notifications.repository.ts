import { Injectable } from '@nestjs/common';
import type { Notification, Prisma } from '../generated/notification-prisma-client';
import { Prisma as NotificationPrisma } from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import type { NormalizedCreateNotificationDto } from './dto/create-notification.dto';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';

export interface PagedNotificationsRow {
  items: Notification[];
  totalItems: number;
}

@Injectable()
export class NotificationsRepository {
  constructor(private readonly prisma: NotificationPrismaService) {}

  async create(dto: NormalizedCreateNotificationDto): Promise<Notification> {
    return this.prisma.notification.create({
      data: {
        userId: dto.userId,
        type: dto.type,
        title: dto.title,
        body: dto.body,
        data: dto.data === null ? NotificationPrisma.DbNull : (dto.data as Prisma.InputJsonValue),
      },
    });
  }

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
