import {
  BadRequestException,
  ConflictException,
  Injectable,
  UnprocessableEntityException,
} from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateOperatorAnnouncementDto } from './dto/create-operator-announcement.dto';
import { IdentityOperatorRecipientProvider } from './identity-operator-recipient.provider';
import { NotificationsService } from './notifications.service';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';

export interface OperatorAnnouncementResult {
  announcementId: string;
  recipientCount: number;
}

@Injectable()
export class OperatorAnnouncementService {
  constructor(
    private readonly notificationsService: NotificationsService,
    private readonly identityRecipients: IdentityOperatorRecipientProvider,
    private readonly tripRecipients: TripAnnouncementRecipientProvider,
    private readonly redis: RedisService,
  ) {}

  async create(
    actorUserId: string,
    operatorId: string,
    idempotencyKey: string | undefined,
    dto: CreateOperatorAnnouncementDto,
  ): Promise<OperatorAnnouncementResult> {
    if (!idempotencyKey?.trim()) {
      throw new BadRequestException({
        errorCode: 'IDEMPOTENCY_KEY_REQUIRED',
        detail: 'Idempotency-Key header is required',
      });
    }
    const responseKey = `notification:operator-announcement:${actorUserId}:${idempotencyKey.trim()}`;
    const existing = await this.redis.get(responseKey);
    if (existing) return JSON.parse(existing) as OperatorAnnouncementResult;
    const lockKey = `${responseKey}:lock`;
    const lockAcquired = await this.redis.getClient().set(lockKey, '1', 'EX', 60, 'NX');
    if (!lockAcquired) {
      const completed = await this.waitForCompletedResponse(responseKey);
      if (completed) return completed;
      throw new ConflictException({
        errorCode: 'IDEMPOTENCY_REQUEST_IN_PROGRESS',
        detail: 'An announcement with this Idempotency-Key is still being processed',
      });
    }

    try {
      const recipients =
        dto.scope === 'TRIP'
          ? await this.tripRecipients.resolveTripCrewUserIds(dto.tripId!, operatorId)
          : await this.identityRecipients.resolveOperatorCrewUserIds(operatorId);
      const uniqueRecipients = [...new Set(recipients)];
      if (uniqueRecipients.length === 0) {
        throw new UnprocessableEntityException({
          errorCode: 'NOTIFICATION_RECIPIENTS_NOT_FOUND',
          detail: 'No active driver or assistant recipient was found',
        });
      }

      const notifications = await Promise.all(
        uniqueRecipients.map((userId) =>
          this.notificationsService.createNotification({
            userId,
            type: NotificationType.OPERATOR_ANNOUNCEMENT,
            title: dto.title,
            body: dto.body,
            data: { scope: dto.scope, operatorId, ...(dto.tripId ? { tripId: dto.tripId } : {}) },
            dedupeKey: `operator-announcement:${actorUserId}:${idempotencyKey.trim()}:${userId}`,
          }),
        ),
      );
      const result = {
        announcementId: notifications[0]!.id,
        recipientCount: uniqueRecipients.length,
      };
      await this.redis.set(responseKey, JSON.stringify(result), 86_400);
      return result;
    } finally {
      await this.redis.getClient().del(lockKey);
    }
  }

  private async waitForCompletedResponse(
    responseKey: string,
  ): Promise<OperatorAnnouncementResult | null> {
    for (let attempt = 0; attempt < 20; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 100));
      const response = await this.redis.get(responseKey);
      if (response) return JSON.parse(response) as OperatorAnnouncementResult;
    }
    return null;
  }
}
