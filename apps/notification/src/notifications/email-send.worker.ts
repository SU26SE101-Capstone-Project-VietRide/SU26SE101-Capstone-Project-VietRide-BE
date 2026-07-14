import { Inject, Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Job, Worker } from 'bullmq';
import IORedis from 'ioredis';
import { EMAIL_PROVIDER, ENV_TOKEN } from '../app/tokens';
import type { Env } from '../config/env.schema';
import { EmailDeliveryStatus } from '../generated/notification-prisma-client';
import {
  EMAIL_LAST_ERROR_MAX_LENGTH,
  EMAIL_SEND_ATTEMPTS,
  EMAIL_SEND_BACKOFF_DELAYS_MS,
  EMAIL_SEND_QUEUE_NAME,
  LAST_EMAIL_SEND_BACKOFF_DELAY_MS,
} from './email-send.constants';
import { BULLMQ_QUEUE_PREFIX } from './fcm-push.constants';
import type { EmailProvider, EmailSendJobData } from './email-send.types';
import { EmailTemplateRenderer } from './email-template.renderer';
import { NotificationsRepository } from './notifications.repository';
import { createNotificationLogger } from './notification-logger';
import { normalizeSafeError } from './safe-error';

@Injectable()
export class EmailSendWorker implements OnModuleInit, OnModuleDestroy {
  private readonly logger = createNotificationLogger(EmailSendWorker.name);
  private connection: IORedis | null = null;
  private worker: Worker<EmailSendJobData> | null = null;

  constructor(
    @Inject(ENV_TOKEN) private readonly env: Env,
    @Inject(EMAIL_PROVIDER) private readonly emailProvider: EmailProvider,
    private readonly emailTemplateRenderer: EmailTemplateRenderer,
    private readonly notificationsRepository: NotificationsRepository,
  ) {}

  onModuleInit(): void {
    this.connection = new IORedis(this.env.REDIS_URL, { maxRetriesPerRequest: null });
    this.worker = new Worker<EmailSendJobData>(EMAIL_SEND_QUEUE_NAME, (job) => this.process(job), {
      connection: this.connection,
      prefix: BULLMQ_QUEUE_PREFIX,
      settings: {
        backoffStrategy: (attemptsMade) =>
          EMAIL_SEND_BACKOFF_DELAYS_MS[attemptsMade - 1] ?? LAST_EMAIL_SEND_BACKOFF_DELAY_MS,
      },
    });
  }

  async onModuleDestroy(): Promise<void> {
    if (this.worker) {
      await this.worker.close();
    }
    if (this.connection) {
      await this.connection.quit();
    }
  }

  async process(job: Job<EmailSendJobData>): Promise<void> {
    const delivery = await this.notificationsRepository.findEmailDeliveryById(
      job.data.emailDeliveryId,
    );
    if (!delivery) {
      this.logger.warn(
        { emailDeliveryId: job.data.emailDeliveryId },
        'Skipping email send for missing delivery',
      );
      return;
    }

    if (
      delivery.status === EmailDeliveryStatus.SENT ||
      delivery.status === EmailDeliveryStatus.FAILED ||
      delivery.status === EmailDeliveryStatus.SENDING
    ) {
      if (delivery.status === EmailDeliveryStatus.SENDING) {
        this.logger.warn(
          { emailDeliveryId: delivery.id },
          'Skipping email delivery with an uncertain provider result',
        );
      }
      return;
    }

    if (!(await this.notificationsRepository.markEmailDeliverySending(delivery.id))) {
      return;
    }

    const currentAttempt = job.attemptsMade + 1;
    let providerMessageId: string | null = null;
    try {
      const renderedEmail = this.emailTemplateRenderer.render(
        job.data.templateKey,
        job.data.templateData,
      );
      const result = await this.emailProvider.send({
        deliveryId: delivery.id,
        toEmail: job.data.toEmail,
        ...renderedEmail,
      });
      providerMessageId = result.messageId ?? null;
    } catch (error) {
      const lastError = this.normalizeError(error);
      if (currentAttempt >= EMAIL_SEND_ATTEMPTS) {
        await this.notificationsRepository.markEmailDeliveryFailed(
          delivery.id,
          currentAttempt,
          lastError,
        );
        return;
      }

      await this.notificationsRepository.markEmailDeliveryRetrying(
        delivery.id,
        currentAttempt,
        lastError,
      );
      throw new Error('EMAIL_SEND_RETRYABLE_FAILURE');
    }

    try {
      await this.notificationsRepository.markEmailDeliverySent(delivery.id, providerMessageId);
    } catch (error) {
      this.logger.error(
        { emailDeliveryId: delivery.id, error: this.normalizeError(error) },
        'Email provider accepted delivery but status persistence is uncertain',
      );
      throw new Error('EMAIL_SEND_STATUS_UNCERTAIN');
    }
  }

  private normalizeError(error: unknown): string {
    return normalizeSafeError(error, EMAIL_LAST_ERROR_MAX_LENGTH);
  }
}
