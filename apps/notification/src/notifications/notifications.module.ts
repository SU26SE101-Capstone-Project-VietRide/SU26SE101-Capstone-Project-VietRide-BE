import { Module } from '@nestjs/common';
import { NOTIFICATION_JWT_VERIFIER } from '../app/tokens';
import { JoseNotificationUserJwtVerifier } from '../auth/user-jwt.verifier';
import { UserJwtAuthGuard } from '../auth/user-jwt-auth.guard';
import { NotificationsController } from './notifications.controller';
import { NotificationsRepository } from './notifications.repository';
import { NotificationsService } from './notifications.service';

@Module({
  controllers: [NotificationsController],
  providers: [
    NotificationsService,
    NotificationsRepository,
    UserJwtAuthGuard,
    { provide: NOTIFICATION_JWT_VERIFIER, useClass: JoseNotificationUserJwtVerifier },
  ],
})
export class NotificationsModule {}
