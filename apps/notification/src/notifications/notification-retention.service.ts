import { Inject, Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { MILLISECONDS_PER_DAY, RETENTION_JOB_NAME } from './notification-retention.constants';
import { NotificationsRepository } from './notifications.repository';
import { createNotificationLogger } from './notification-logger';

@Injectable()
export class NotificationRetentionService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = createNotificationLogger(NotificationRetentionService.name);
  private interval: NodeJS.Timeout | null = null;

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    private readonly notificationsRepository: NotificationsRepository,
  ) {}

  onModuleInit(): void {
    this.interval = setInterval(
      () => void this.runRetention(),
      this.env.NOTIFICATION_RETENTION_JOB_INTERVAL_MS,
    );
    this.interval.unref();
    void this.runRetention();
  }

  onModuleDestroy(): void {
    if (this.interval) {
      clearInterval(this.interval);
      this.interval = null;
    }
  }

  async runRetention(now: Date = new Date()): Promise<number> {
    const cutoff = this.calculateCutoff(now);
    const deletedCount =
      await this.notificationsRepository.deleteNotificationsCreatedBefore(cutoff);
    this.logger.info(
      {
        job: RETENTION_JOB_NAME,
        cutoff: cutoff.toISOString(),
        deletedCount,
      },
      'Completed notification retention cleanup',
    );

    return deletedCount;
  }

  calculateCutoff(now: Date): Date {
    return new Date(now.getTime() - this.env.NOTIFICATION_RETENTION_DAYS * MILLISECONDS_PER_DAY);
  }
}
