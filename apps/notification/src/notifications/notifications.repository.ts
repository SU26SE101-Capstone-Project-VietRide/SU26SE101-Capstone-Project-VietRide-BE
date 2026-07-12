import { Injectable } from '@nestjs/common';
import type {
  EmailDelivery,
  EmailTemplateKey,
  Notification,
  NotificationDelivery,
  Prisma,
} from '../generated/notification-prisma-client';
import {
  EmailDeliveryStatus,
  NotificationDeliveryStatus,
  Prisma as NotificationPrisma,
} from '../generated/notification-prisma-client';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import type { NormalizedCreateNotificationDto } from './dto/create-notification.dto';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';
import type { DeviceTokenSnapshot } from './fcm-push.types';

export interface PagedNotificationsRow {
  items: Notification[];
  totalItems: number;
}

export interface CreateEmailDeliveryRow {
  notificationId?: string | null;
  toEmail: string;
  templateKey: EmailTemplateKey;
  subject: string;
  sanitizedData: unknown;
}

export interface CreateNotificationResult {
  notification: Notification;
  created: boolean;
}

@Injectable()
export class NotificationsRepository {
  constructor(private readonly prisma: NotificationPrismaService) {}

  async create(dto: NormalizedCreateNotificationDto): Promise<CreateNotificationResult> {
    try {
      const notification = await this.prisma.notification.create({
        data: {
          userId: dto.userId,
          type: dto.type,
          title: dto.title,
          body: dto.body,
          data: dto.data === null ? NotificationPrisma.DbNull : (dto.data as Prisma.InputJsonValue),
          dedupeKey: dto.dedupeKey ?? null,
        },
      });

      return { notification, created: true };
    } catch (error) {
      if (!dto.dedupeKey || !isUniqueConstraintError(error)) {
        throw error;
      }

      const notification = await this.prisma.notification.findUnique({
        where: { dedupeKey: dto.dedupeKey },
      });
      if (!notification) {
        throw error;
      }

      return { notification, created: false };
    }
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

  async findById(notificationId: string): Promise<Notification | null> {
    return this.prisma.notification.findUnique({
      where: { id: notificationId },
    });
  }

  async markRead(notificationId: string): Promise<Notification> {
    return this.prisma.notification.update({
      where: { id: notificationId },
      data: { readAt: new Date() },
    });
  }

  async deleteNotificationsCreatedBefore(cutoff: Date): Promise<number> {
    const result = await this.prisma.notification.deleteMany({
      where: {
        createdAt: {
          lt: cutoff,
        },
      },
    });

    return result.count;
  }

  async listDeliveriesByNotificationId(notificationId: string): Promise<NotificationDelivery[]> {
    return this.prisma.notificationDelivery.findMany({
      where: { notificationId },
      orderBy: { createdAt: 'asc' },
    });
  }

  async createDelivery(
    notificationId: string,
    deviceToken: DeviceTokenSnapshot,
  ): Promise<NotificationDelivery> {
    return this.prisma.notificationDelivery.upsert({
      where: {
        notificationId_fcmToken: {
          notificationId,
          fcmToken: deviceToken.fcmToken,
        },
      },
      create: {
        notificationId,
        fcmToken: deviceToken.fcmToken,
        platform: deviceToken.platform,
      },
      update: {},
    });
  }

  async markDeliverySent(
    deliveryId: string,
    providerMessageId: string | null,
  ): Promise<NotificationDelivery> {
    return this.prisma.notificationDelivery.update({
      where: { id: deliveryId },
      data: {
        status: NotificationDeliveryStatus.SENT,
        sentAt: new Date(),
        lastError: null,
        providerMessageId,
      },
    });
  }

  async markDeliveryValidated(
    deliveryId: string,
    providerMessageId: string | null,
  ): Promise<NotificationDelivery> {
    return this.prisma.notificationDelivery.update({
      where: { id: deliveryId },
      data: {
        status: NotificationDeliveryStatus.VALIDATED,
        sentAt: new Date(),
        lastError: null,
        providerMessageId,
      },
    });
  }

  async markDeliveryRetrying(
    deliveryId: string,
    retryCount: number,
    lastError: string,
  ): Promise<NotificationDelivery> {
    return this.prisma.notificationDelivery.update({
      where: { id: deliveryId },
      data: {
        status: NotificationDeliveryStatus.RETRYING,
        retryCount,
        lastError,
      },
    });
  }

  async markDeliveryFailed(
    deliveryId: string,
    retryCount: number,
    lastError: string,
  ): Promise<NotificationDelivery> {
    return this.prisma.notificationDelivery.update({
      where: { id: deliveryId },
      data: {
        status: NotificationDeliveryStatus.FAILED,
        retryCount,
        lastError,
      },
    });
  }

  async createEmailDelivery(dto: CreateEmailDeliveryRow): Promise<EmailDelivery> {
    return this.prisma.emailDelivery.create({
      data: {
        notificationId: dto.notificationId ?? null,
        toEmail: dto.toEmail,
        templateKey: dto.templateKey,
        subject: dto.subject,
        sanitizedData: dto.sanitizedData as Prisma.InputJsonValue,
      },
    });
  }

  async findEmailDeliveryById(emailDeliveryId: string): Promise<EmailDelivery | null> {
    return this.prisma.emailDelivery.findUnique({
      where: { id: emailDeliveryId },
    });
  }

  async markEmailDeliverySent(
    emailDeliveryId: string,
    providerMessageId: string | null,
  ): Promise<EmailDelivery> {
    return this.prisma.emailDelivery.update({
      where: { id: emailDeliveryId },
      data: {
        status: EmailDeliveryStatus.SENT,
        providerMessageId,
        sentAt: new Date(),
        lastError: null,
      },
    });
  }

  async markEmailDeliveryRetrying(
    emailDeliveryId: string,
    retryCount: number,
    lastError: string,
  ): Promise<EmailDelivery> {
    return this.prisma.emailDelivery.update({
      where: { id: emailDeliveryId },
      data: {
        status: EmailDeliveryStatus.RETRYING,
        retryCount,
        lastError,
      },
    });
  }

  async markEmailDeliveryFailed(
    emailDeliveryId: string,
    retryCount: number,
    lastError: string,
  ): Promise<EmailDelivery> {
    return this.prisma.emailDelivery.update({
      where: { id: emailDeliveryId },
      data: {
        status: EmailDeliveryStatus.FAILED,
        retryCount,
        lastError,
      },
    });
  }
}

function isUniqueConstraintError(error: unknown): boolean {
  return (
    error instanceof NotificationPrisma.PrismaClientKnownRequestError &&
    error.code === 'P2002'
  );
}
