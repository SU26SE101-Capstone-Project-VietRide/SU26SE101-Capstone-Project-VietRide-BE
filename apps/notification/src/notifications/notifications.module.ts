import { Module } from '@nestjs/common';
import {
  DEVICE_TOKEN_PROVIDER,
  EMAIL_PROVIDER,
  ENV_TOKEN,
  FCM_PUSH_PROVIDER,
  NOTIFICATION_JWT_VERIFIER,
} from '../app/tokens';
import type { Env } from '../config/env.schema';
import { InternalJwtAuthGuard } from '../auth/internal-jwt-auth.guard';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import { CoreEventsConsumer } from './core-events.consumer';
import { BookingCreatedEventsConsumer } from './booking-created-events.consumer';
import { EmailSendQueue } from './email-send.queue';
import { EmailSendWorker } from './email-send.worker';
import { EmailDeliveryRecoveryService } from './email-delivery-recovery.service';
import { EmailTemplateRenderer } from './email-template.renderer';
import { FcmPushQueue } from './fcm-push.queue';
import { FcmPushWorker } from './fcm-push.worker';
import { FakeFcmPushProvider } from './fake-fcm-push.provider';
import { FakeEmailProvider } from './fake-email.provider';
import { FirebaseFcmPushProvider } from './firebase-fcm-push.provider';
import { IdentityDeviceTokenProvider } from './identity-device-token.provider';
import { IdentityOperatorRecipientProvider } from './identity-operator-recipient.provider';
import { InternalEmailsController } from './internal-emails.controller';
import { MessageIdempotencyService } from './message-idempotency.service';
import { NotificationsController } from './notifications.controller';
import { OperatorNotificationsController } from './operator-notifications.controller';
import { OperatorAnnouncementService } from './operator-announcement.service';
import { NotificationRetentionService } from './notification-retention.service';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';
import { SendGridEmailProvider } from './sendgrid-email.provider';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';
import { TripAnnouncementRecipientProvider } from './trip-announcement-recipient.provider';
import { ShuttleEventsConsumer } from './shuttle-events.consumer';
import { BookingTripChangeEventsConsumer } from './booking-trip-change-events.consumer';
import { Day24DepartedPendingEventsConsumer } from './day24-departed-pending-events.consumer';
import { Day24NoShowEventsConsumer } from './day24-no-show-events.consumer';
import { Day24StopDisabledAutoFallbackEventsConsumer } from './day24-stop-disabled-auto-fallback-events.consumer';
import { BookingTripRecipientProvider } from './booking-trip-recipient.provider';
import { ParcelRecipientProvider } from './parcel-recipient.provider';
import { IdentitySystemAdminRecipientProvider } from './identity-system-admin-recipient.provider';

@Module({
  controllers: [NotificationsController, OperatorNotificationsController, InternalEmailsController],
  providers: [
    NotificationsService,
    NotificationsRepository,
    NotificationRetentionService,
    OperatorAnnouncementService,
    TripAnnouncementRecipientProvider,
    BookingTripRecipientProvider,
    ParcelRecipientProvider,
    IdentityOperatorRecipientProvider,
    IdentitySystemAdminRecipientProvider,
    FcmPushQueue,
    FcmPushWorker,
    EmailSendQueue,
    EmailSendWorker,
    EmailDeliveryRecoveryService,
    EmailTemplateRenderer,
    MessageIdempotencyService,
    CoreEventsConsumer,
    BookingCreatedEventsConsumer,
    BookingTripChangeEventsConsumer,
    Day24StopDisabledAutoFallbackEventsConsumer,
    Day24NoShowEventsConsumer,
    Day24DepartedPendingEventsConsumer,
    TripTrackingAlertEventsConsumer,
    ParcelSubscriptionOperatorEventsConsumer,
    ShuttleEventsConsumer,
    UserJwtAuthGuard,
    InternalJwtAuthGuard,
    { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
    { provide: OPERATOR_RECIPIENT_PROVIDER, useExisting: IdentityOperatorRecipientProvider },
    { provide: DEVICE_TOKEN_PROVIDER, useClass: IdentityDeviceTokenProvider },
    {
      provide: FCM_PUSH_PROVIDER,
      inject: [ENV_TOKEN],
      useFactory: (env: Env) => {
        const hasFirebaseCredentials = Boolean(
          env.FCM_PROJECT_ID && env.FCM_CLIENT_EMAIL && env.FCM_PRIVATE_KEY,
        );
        return env.NODE_ENV !== 'production' && !hasFirebaseCredentials && !env.FCM_DRY_RUN
          ? new FakeFcmPushProvider()
          : new FirebaseFcmPushProvider(env);
      },
    },
    {
      provide: EMAIL_PROVIDER,
      inject: [ENV_TOKEN],
      useFactory: (env: Env) =>
        env.NODE_ENV !== 'production' && !env.SENDGRID_API_KEY
          ? new FakeEmailProvider()
          : new SendGridEmailProvider(env),
    },
  ],
  exports: [
    NotificationsService,
    MessageIdempotencyService,
    OPERATOR_RECIPIENT_PROVIDER,
    IdentitySystemAdminRecipientProvider,
  ],
})
export class NotificationsModule {}
