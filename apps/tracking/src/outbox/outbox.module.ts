import { Module } from '@nestjs/common';
import { TrackingPrismaModule } from '../prisma/prisma.module';
import { OutboxPublisherService } from './outbox-publisher.service';
import { OutboxQueueService } from './outbox-queue.service';
import { OutboxRepository } from './outbox.repository';

@Module({
  imports: [TrackingPrismaModule],
  providers: [OutboxRepository, OutboxPublisherService, OutboxQueueService],
  exports: [OutboxPublisherService],
})
export class OutboxModule {}
