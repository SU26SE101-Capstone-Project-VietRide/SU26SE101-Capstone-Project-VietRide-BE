import { Module } from '@nestjs/common';
import { IdentityEventsConsumer } from './identity-events.consumer';

@Module({
  providers: [IdentityEventsConsumer],
})
export class IdentityEventsModule {}
