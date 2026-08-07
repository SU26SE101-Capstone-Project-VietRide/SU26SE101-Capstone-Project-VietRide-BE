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
  hasMore?: boolean;
}

export interface NotificationCursorBoundary {
  snapshotCutoff: Date;
  lastCreatedAt?: Date;
  lastId?: string;
  skip?: number;
}

export interface CreateEmailDeliveryRow {
  notificationId?: string | null;
  dedupeKey?: string;
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

  async listForUser(
    userId: string,
    query: ListNotificationsQueryDto,
    boundary: NotificationCursorBoundary,
  ): Promise<PagedNotificationsRow> {
    const where: Prisma.NotificationWhereInput = {
      userId,
      ...(query.unreadOnly ? { readAt: null } : {}),
      createdAt: { lte: boundary.snapshotCutoff },
      ...(boundary.lastCreatedAt && boundary.lastId
        ? {
            OR: [
              { createdAt: { lt: boundary.lastCreatedAt } },
              { createdAt: boundary.lastCreatedAt, id: { lt: boundary.lastId } },
            ],
          }
        : {}),
    };
    const orderBy: Prisma.NotificationOrderByWithRelationInput[] = [
      { createdAt: 'desc' },
      { id: 'desc' },
    ];

    const [items, totalItems] = await Promise.all([
      this.prisma.notification.findMany({
        where,
        orderBy,
        ...(boundary.skip ? { skip: boundary.skip } : {}),
        take: query.pageSize + 1,
      }),
      this.prisma.notification.count({
        where: {
          userId,
          ...(query.unreadOnly ? { readAt: null } : {}),
          createdAt: { lte: boundary.snapshotCutoff },
        },
      }),
    ]);

    return {
      items: items.slice(0, query.pageSize),
      totalItems,
      hasMore: items.length > query.pageSize,
    };
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

  async markAllRead(userId: string, cutoff: Date): Promise<number> {
    const result = await this.prisma.notification.updateMany({
      where: {
        userId,
        readAt: null,
        createdAt: { lte: cutoff },
      },
      data: { readAt: cutoff },
    });
    return result.count;
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

  async createEmailDelivery(
    dto: CreateEmailDeliveryRow,
  ): Promise<{ delivery: EmailDelivery; created: boolean }> {
    try {
      const delivery = await this.prisma.emailDelivery.create({
        data: {
          notificationId: dto.notificationId ?? null,
          dedupeKey: dto.dedupeKey ?? null,
          toEmail: dto.toEmail,
          templateKey: dto.templateKey,
          subject: dto.subject,
          sanitizedData: dto.sanitizedData as Prisma.InputJsonValue,
        },
      });
      return { delivery, created: true };
    } catch (error) {
      if (!dto.dedupeKey || !isUniqueConstraintError(error)) {
        throw error;
      }
      const delivery = await this.prisma.emailDelivery.findUnique({
        where: { dedupeKey: dto.dedupeKey },
      });
      if (!delivery) {
        throw error;
      }
      return { delivery, created: false };
    }
  }

  async findEmailDeliveryById(emailDeliveryId: string): Promise<EmailDelivery | null> {
    return this.prisma.emailDelivery.findUnique({
      where: { id: emailDeliveryId },
    });
  }

  async markEmailDeliverySending(
    emailDeliveryId: string,
    leaseCutoff: Date,
  ): Promise<string | null> {
    const [claimed] = await this.prisma.$queryRaw<Array<{ claimToken: string }>>(
      NotificationPrisma.sql`
        UPDATE "vietride_notification"."email_deliveries"
        SET "status" = 'SENDING'
        WHERE "id" = ${emailDeliveryId}::uuid
          AND (
            "status" IN ('PENDING', 'RETRYING')
            OR ("status" = 'SENDING' AND "updated_at" <= ${leaseCutoff})
          )
        RETURNING "updated_at"::text AS "claimToken"
      `,
    );
    return claimed?.claimToken ?? null;
  }

  async listStaleSendingEmailDeliveryIds(leaseCutoff: Date, take: number): Promise<string[]> {
    const deliveries = await this.prisma.emailDelivery.findMany({
      where: {
        status: EmailDeliveryStatus.SENDING,
        updatedAt: { lte: leaseCutoff },
      },
      orderBy: { updatedAt: 'asc' },
      take,
      select: { id: true },
    });
    return deliveries.map(({ id }) => id);
  }

  async markEmailDeliverySent(
    emailDeliveryId: string,
    providerMessageId: string | null,
    claimToken: string,
  ): Promise<boolean> {
    const count = await this.prisma.$executeRaw(
      NotificationPrisma.sql`
        UPDATE "vietride_notification"."email_deliveries"
        SET "status" = 'SENT',
            "provider_message_id" = ${providerMessageId},
            "sent_at" = ${new Date()},
            "last_error" = NULL
        WHERE "id" = ${emailDeliveryId}::uuid
          AND "status" = 'SENDING'
          AND "updated_at" = ${claimToken}::timestamptz
      `,
    );
    return count === 1;
  }

  async markEmailDeliveryRetrying(
    emailDeliveryId: string,
    retryCount: number,
    lastError: string,
    claimToken: string,
  ): Promise<boolean> {
    const count = await this.prisma.$executeRaw(
      NotificationPrisma.sql`
        UPDATE "vietride_notification"."email_deliveries"
        SET "status" = 'RETRYING',
            "retry_count" = ${retryCount},
            "last_error" = ${lastError}
        WHERE "id" = ${emailDeliveryId}::uuid
          AND "status" = 'SENDING'
          AND "updated_at" = ${claimToken}::timestamptz
      `,
    );
    return count === 1;
  }

  async markEmailDeliveryFailed(
    emailDeliveryId: string,
    retryCount: number,
    lastError: string,
    claimToken: string,
  ): Promise<boolean> {
    const count = await this.prisma.$executeRaw(
      NotificationPrisma.sql`
        UPDATE "vietride_notification"."email_deliveries"
        SET "status" = 'FAILED',
            "retry_count" = ${retryCount},
            "last_error" = ${lastError}
        WHERE "id" = ${emailDeliveryId}::uuid
          AND "status" = 'SENDING'
          AND "updated_at" = ${claimToken}::timestamptz
      `,
    );
    return count === 1;
  }
}

function isUniqueConstraintError(error: unknown): boolean {
  return (
    error instanceof NotificationPrisma.PrismaClientKnownRequestError && error.code === 'P2002'
  );
}
