import { Module } from '@nestjs/common';
import {
  DEVICE_TOKEN_PROVIDER,
  EMAIL_PROVIDER,
  FCM_PUSH_PROVIDER,
  NOTIFICATION_JWT_VERIFIER,
} from '../app/tokens';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import { CoreEventsConsumer } from './core-events.consumer';
import { EmailSendQueue } from './email-send.queue';
import { EmailSendWorker } from './email-send.worker';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
import { FcmPushWorker } from './fcm-push.worker';
import { FirebaseFcmPushProvider } from './firebase-fcm-push.provider';
import { IdentityDeviceTokenProvider } from './identity-device-token.provider';
import { IdentityOperatorRecipientProvider } from './identity-operator-recipient.provider';
import { InternalEmailsController } from './internal-emails.controller';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsController } from './notifications.controller';
import { NotificationRetentionService } from './notification-retention.service';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';
import { SendGridEmailProvider } from './sendgrid-email.provider';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

@Module({
  controllers: [NotificationsController, InternalEmailsController],
  providers: [
    NotificationsService,
    NotificationsRepository,
    NotificationRetentionService,
    FcmPushQueue,
    FcmPushWorker,
    EmailSendQueue,
    EmailSendWorker,
    EmailTemplateRenderer,
    MessageIdempotencyService,
    CoreEventsConsumer,
    TripTrackingAlertEventsConsumer,
    ParcelSubscriptionOperatorEventsConsumer,
    UserJwtAuthGuard,
    InternalJwtAuthGuard,
    { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
    { provide: OPERATOR_RECIPIENT_PROVIDER, useClass: IdentityOperatorRecipientProvider },
    { provide: DEVICE_TOKEN_PROVIDER, useClass: IdentityDeviceTokenProvider },
    { provide: FCM_PUSH_PROVIDER, useClass: FirebaseFcmPushProvider },
    { provide: EMAIL_PROVIDER, useClass: SendGridEmailProvider },
  ],
  exports: [NotificationsService, MessageIdempotencyService, OPERATOR_RECIPIENT_PROVIDER],
})
export class NotificationsModule {}
