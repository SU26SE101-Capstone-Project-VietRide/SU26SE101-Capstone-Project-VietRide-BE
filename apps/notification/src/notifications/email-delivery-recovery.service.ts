import { Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import {
  EMAIL_RECOVERY_BATCH_SIZE,
  EMAIL_RECOVERY_INTERVAL_MS,
  EMAIL_LAST_ERROR_MAX_LENGTH,
  EMAIL_SENDING_LEASE_MS,
} from './email-send.constants';
import { EmailSendQueue } from './email-send.queue';
import { NotificationsRepository } from './notifications.repository';
import { createNotificationLogger } from './notification-logger';
import { normalizeSafeError } from './safe-error';

@Injectable()
export class EmailDeliveryRecoveryService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = createNotificationLogger(EmailDeliveryRecoveryService.name);
  private interval: NodeJS.Timeout | null = null;

  constructor(
    private readonly notificationsRepository: NotificationsRepository,
    private readonly emailSendQueue: EmailSendQueue,
  ) {}

  onModuleInit(): void {
    this.interval = setInterval(() => void this.runSafely(), EMAIL_RECOVERY_INTERVAL_MS);
    this.interval.unref();
    void this.runSafely();
  }

  onModuleDestroy(): void {
    if (this.interval) {
      clearInterval(this.interval);
      this.interval = null;
    }
  }

  async runRecovery(now: Date = new Date()): Promise<number> {
    const leaseCutoff = new Date(now.getTime() - EMAIL_SENDING_LEASE_MS);
    const deliveryIds = await this.notificationsRepository.listStaleSendingEmailDeliveryIds(
      leaseCutoff,
      EMAIL_RECOVERY_BATCH_SIZE,
    );
    let recoveredCount = 0;

    for (const emailDeliveryId of deliveryIds) {
      try {
        if (await this.emailSendQueue.retryRetained(emailDeliveryId)) recoveredCount += 1;
      } catch (error) {
        this.logger.error(
          {
            emailDeliveryId,
            error: normalizeSafeError(error, EMAIL_LAST_ERROR_MAX_LENGTH),
          },
          'Failed to recover stale email delivery',
        );
      }
    }

    this.logger.info(
      { scannedCount: deliveryIds.length, recoveredCount },
      'Completed stale email delivery recovery',
    );
    return recoveredCount;
  }

  private async runSafely(): Promise<void> {
    try {
      await this.runRecovery();
    } catch (error) {
      this.logger.error(
        { error: normalizeSafeError(error, EMAIL_LAST_ERROR_MAX_LENGTH) },
        'Stale email delivery recovery failed',
      );
    }
  }
}
