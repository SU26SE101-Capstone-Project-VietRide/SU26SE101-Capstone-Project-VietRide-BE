import { BadRequestException, Injectable, NotFoundException, Optional } from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { z } from 'zod';
import { EmailDeliveryStatus, type Notification } from '../generated/notification-prisma-client';
import {
  CreateNotificationSchema,
  type CreateNotificationDto,
} from './dto/create-notification.dto';
import { CreateEmailSendSchema, type CreateEmailSendDto } from './dto/create-email-send.dto';
import type { ListNotificationsQueryDto } from './dto/list-notifications-query.dto';
import { EmailSendQueue } from './email-send.queue';
import { sanitizeEmailTemplateData } from './email-sensitive-data';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
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
  nextCursor: string | null;
}

export interface MarkAllReadDto {
  markedCount: number;
  readAt: string;
}

const NotificationCursorSchema = z.object({
  version: z.literal(1),
  userId: z.string().uuid(),
  snapshotCutoff: z.string().datetime(),
  lastCreatedAt: z.string().datetime(),
  lastId: z.string().uuid(),
  unreadOnly: z.boolean(),
  pageSize: z.number().int().min(1).max(100),
  pageIndex: z.number().int().min(2),
});

export interface EmailDeliveryDto {
  id: string;
  toEmail: string;
  templateKey: string;
  status: string;
  createdAt: string;
}

@Injectable()
export class NotificationsService {
  constructor(
    private readonly notificationsRepository: NotificationsRepository,
    private readonly fcmPushQueue: FcmPushQueue,
    private readonly emailSendQueue: EmailSendQueue,
    private readonly emailTemplateRenderer: EmailTemplateRenderer,
    @Optional() private readonly redis?: RedisService,
  ) {}

  async createNotification(dto: CreateNotificationDto): Promise<NotificationItemDto> {
    const result = await this.notificationsRepository.create(CreateNotificationSchema.parse(dto));
    const notification = result.notification;
    await this.fcmPushQueue.enqueue({
      notificationId: notification.id,
      userId: notification.userId,
    });

    return this.toDto(notification);
  }

  async listForUser(
    userId: string,
    query: ListNotificationsQueryDto,
  ): Promise<PagedNotificationsDto> {
    const cursor = query.cursor ? this.decodeCursor(query.cursor, userId) : null;
    const effectiveQuery = cursor
      ? { ...query, unreadOnly: cursor.unreadOnly, pageSize: cursor.pageSize, page: cursor.pageIndex }
      : query;
    const snapshotCutoff = cursor ? new Date(cursor.snapshotCutoff) : new Date();
    const result = await this.notificationsRepository.listForUser(userId, effectiveQuery, {
      snapshotCutoff,
      ...(cursor
        ? { lastCreatedAt: new Date(cursor.lastCreatedAt), lastId: cursor.lastId }
        : effectiveQuery.page > 1
          ? { skip: (effectiveQuery.page - 1) * effectiveQuery.pageSize }
          : {}),
    });
    const totalPages = Math.ceil(result.totalItems / effectiveQuery.pageSize);
    const last = result.items.at(-1);
    const hasMore = result.hasMore ?? effectiveQuery.page < totalPages;
    const nextCursor = hasMore && last
      ? this.encodeCursor({
          version: 1,
          userId,
          snapshotCutoff: snapshotCutoff.toISOString(),
          lastCreatedAt: last.createdAt.toISOString(),
          lastId: last.id,
          unreadOnly: effectiveQuery.unreadOnly,
          pageSize: effectiveQuery.pageSize,
          pageIndex: effectiveQuery.page + 1,
        })
      : null;

    return {
      items: result.items.map((notification) => this.toDto(notification)),
      page: effectiveQuery.page,
      pageSize: effectiveQuery.pageSize,
      totalItems: result.totalItems,
      totalPages,
      hasNextPage: hasMore,
      hasPreviousPage: effectiveQuery.page > 1,
      nextCursor,
    };
  }

  async markAllRead(userId: string, idempotencyKey: string): Promise<MarkAllReadDto> {
    if (!this.redis) throw new Error('Redis is required for notification read-all idempotency.');
    const key = `notification:read-all:${userId}:${idempotencyKey}`;
    const proposedCutoff = new Date().toISOString();
    const inserted = await this.redis.getClient().set(key, proposedCutoff, 'EX', 86_400, 'NX');
    const cutoffValue = inserted === 'OK' ? proposedCutoff : await this.redis.get(key);
    if (!cutoffValue) throw new Error('Notification read-all cutoff could not be persisted.');
    const cutoff = new Date(cutoffValue);
    if (Number.isNaN(cutoff.getTime())) throw new Error('Notification read-all cutoff is invalid.');

    const markedCount = await this.notificationsRepository.markAllRead(userId, cutoff);
    return { markedCount, readAt: cutoff.toISOString() };
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

  async enqueueEmail(dto: CreateEmailSendDto): Promise<EmailDeliveryDto> {
    const normalizedDto = CreateEmailSendSchema.parse(dto);
    const renderedEmail = this.emailTemplateRenderer.render(
      normalizedDto.templateKey,
      normalizedDto.templateData,
    );
    const result = await this.notificationsRepository.createEmailDelivery({
      notificationId: normalizedDto.notificationId ?? null,
      ...(normalizedDto.dedupeKey ? { dedupeKey: normalizedDto.dedupeKey } : {}),
      toEmail: normalizedDto.toEmail,
      templateKey: normalizedDto.templateKey,
      subject: renderedEmail.subject,
      sanitizedData: sanitizeEmailTemplateData(normalizedDto.templateData),
    });

    const emailDelivery = result.delivery;
    if (
      result.created ||
      emailDelivery.status === EmailDeliveryStatus.PENDING ||
      emailDelivery.status === EmailDeliveryStatus.RETRYING ||
      emailDelivery.status === EmailDeliveryStatus.SENDING
    ) {
      await this.emailSendQueue.enqueue({
        emailDeliveryId: emailDelivery.id,
        toEmail: normalizedDto.toEmail,
        templateKey: normalizedDto.templateKey,
        templateData: normalizedDto.templateData,
      });
    }

    return {
      id: emailDelivery.id,
      toEmail: emailDelivery.toEmail,
      templateKey: emailDelivery.templateKey,
      status: emailDelivery.status,
      createdAt: emailDelivery.createdAt.toISOString(),
    };
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

  private encodeCursor(cursor: z.infer<typeof NotificationCursorSchema>): string {
    return Buffer.from(JSON.stringify(cursor), 'utf8').toString('base64url');
  }

  private decodeCursor(value: string, userId: string): z.infer<typeof NotificationCursorSchema> {
    try {
      const decoded = JSON.parse(Buffer.from(value, 'base64url').toString('utf8')) as unknown;
      const cursor = NotificationCursorSchema.parse(decoded);
      if (cursor.userId !== userId || new Date(cursor.snapshotCutoff).getTime() > Date.now()) {
        throw new Error('Cursor scope is invalid.');
      }
      return cursor;
    } catch {
      throw new BadRequestException({
        errorCode: 'VALIDATION_FAILED',
        detail: 'Notification cursor is invalid',
      });
    }
  }
}
