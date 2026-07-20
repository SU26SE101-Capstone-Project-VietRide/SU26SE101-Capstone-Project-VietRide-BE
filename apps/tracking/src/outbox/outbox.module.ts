import { Module } from '@nestjs/common';
import { TrackingPrismaModule } from '../prisma/prisma.module';
import { OutboxPublisherService } from './outbox-publisher.service';
import { OutboxQueueService } from './outbox-queue.service';
import { OutboxRepository } from './outbox.repository';
import { OutboxDlqController } from './outbox-dlq.controller';
import { OutboxDlqService } from './outbox-dlq.service';
import { TrackingInternalJwtGuard } from './tracking-internal-jwt.guard';

@Module({
  imports: [TrackingPrismaModule],
  controllers: [OutboxDlqController],
  providers: [
    OutboxRepository,
    OutboxDlqService,
    OutboxPublisherService,
    OutboxQueueService,
    TrackingInternalJwtGuard,
  ],
  exports: [OutboxPublisherService],
})
export class OutboxModule {}
