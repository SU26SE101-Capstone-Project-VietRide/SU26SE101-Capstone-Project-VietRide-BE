import {
  BadRequestException,
  ConflictException,
  Injectable,
  UnprocessableEntityException,
} from '@nestjs/common';
import { RedisService } from '@vietride/nest-redis';
import { createHash, randomUUID } from 'node:crypto';
import { NotificationType } from '../generated/notification-prisma-client';
import type { CreateOperatorAnnouncementDto } from './dto/create-operator-announcement.dto';
import { IdentityOperatorRecipientProvider } from './identity-operator-recipient.provider';
import { NotificationsService } from './notifications.service';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';
import { requireUuidV4IdempotencyKey } from '../swagger/idempotency-key';

export interface OperatorAnnouncementResult {
  announcementId: string;
  recipientCount: number;
}

@Injectable()
export class OperatorAnnouncementService {
  private static readonly RELEASE_LOCK_SCRIPT = `
    if redis.call('GET', KEYS[1]) == ARGV[1] then
      return redis.call('DEL', KEYS[1])
    end
    return 0
  `;

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
    const normalizedKey = requireUuidV4IdempotencyKey(idempotencyKey);
    const fingerprint = createHash('sha256')
      .update(JSON.stringify({ actorUserId, operatorId, dto }))
      .digest('hex')
      .toUpperCase();
    const responseKey = `notification:operator-announcement:${actorUserId}:${normalizedKey}`;
    const existing = await this.redis.get(responseKey);
    if (existing) return this.parseStoredResponse(existing, fingerprint);
    const lockKey = `${responseKey}:lock`;
    const ownerToken = randomUUID();
    const lockAcquired = await this.redis
      .getClient()
      .set(lockKey, ownerToken, 'EX', 60, 'NX');
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
            dedupeKey: `operator-announcement:${actorUserId}:${normalizedKey}:${userId}`,
          }),
        ),
      );
      const result = {
        announcementId: notifications[0]!.id,
        recipientCount: uniqueRecipients.length,
      };
      await this.redis.set(responseKey, JSON.stringify({ fingerprint, result }), 86_400);
      return result;
    } finally {
      await this.redis
        .getClient()
        .eval(OperatorAnnouncementService.RELEASE_LOCK_SCRIPT, 1, lockKey, ownerToken);
    }
  }

  private async waitForCompletedResponse(
    responseKey: string,
  ): Promise<OperatorAnnouncementResult | null> {
    for (let attempt = 0; attempt < 20; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 100));
      const response = await this.redis.get(responseKey);
      if (response) {
        const stored = JSON.parse(response) as {
          result?: OperatorAnnouncementResult;
          announcementId?: string;
          recipientCount?: number;
        };
        return stored.result ?? (stored as OperatorAnnouncementResult);
      }
    }
    return null;
  }

  private parseStoredResponse(
    value: string,
    fingerprint: string,
  ): OperatorAnnouncementResult {
    const stored = JSON.parse(value) as {
      fingerprint?: string;
      result?: OperatorAnnouncementResult;
      announcementId?: string;
      recipientCount?: number;
    };
    if (stored.fingerprint && stored.fingerprint !== fingerprint) {
      throw new BadRequestException({
        errorCode: 'IDEMPOTENCY_KEY_MISMATCH',
        detail: 'The Idempotency-Key was reused with a different request',
      });
    }
    return stored.result ?? (stored as OperatorAnnouncementResult);
  }
}
