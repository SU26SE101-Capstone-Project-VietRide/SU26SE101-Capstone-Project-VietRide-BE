import { Module } from '@nestjs/common';
import { NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import { CoreEventsConsumer } from './core-events.consumer';
import { NoopOperatorRecipientProvider } from './noop-operator-recipient.provider';
import { NotificationsController } from './notifications.controller';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';
import { OPERATOR_RECIPIENT_PROVIDER } from './parcel-subscription-operator-events.constants';
import { ParcelSubscriptionOperatorEventsConsumer } from './parcel-subscription-operator-events.consumer';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

@Module({
  controllers: [NotificationsController],
  providers: [
    NotificationsService,
    NotificationsRepository,
    CoreEventsConsumer,
    TripTrackingAlertEventsConsumer,
    ParcelSubscriptionOperatorEventsConsumer,
    UserJwtAuthGuard,
    { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
    { provide: OPERATOR_RECIPIENT_PROVIDER, useClass: NoopOperatorRecipientProvider },
  ],
})
export class NotificationsModule {}
