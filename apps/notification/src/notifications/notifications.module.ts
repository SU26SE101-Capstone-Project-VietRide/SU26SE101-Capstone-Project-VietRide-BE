import { Module } from '@nestjs/common';
import { NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import { CoreEventsConsumer } from './core-events.consumer';
import { NotificationsController } from './notifications.controller';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';
import { TripTrackingAlertEventsConsumer } from './trip-tracking-alert-events.consumer';

@Module({
  controllers: [NotificationsController],
  providers: [
    NotificationsService,
    NotificationsRepository,
    CoreEventsConsumer,
    TripTrackingAlertEventsConsumer,
    UserJwtAuthGuard,
    { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
  ],
})
export class NotificationsModule {}
