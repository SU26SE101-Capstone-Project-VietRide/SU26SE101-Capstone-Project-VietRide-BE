import { Module } from '@nestjs/common';
import { NotificationsModule } from '../notifications/notifications.module';
import { IdentityEventsConsumer } from './identity-events.consumer';

@Module({
  imports: [NotificationsModule],
  providers: [IdentityEventsConsumer],
})
export class IdentityEventsModule {}
