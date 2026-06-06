import { Global, Module } from '@nestjs/common';
import { NotificationPrismaService } from './notification-prisma.service';

@Global()
@Module({
  providers: [NotificationPrismaService],
  exports: [NotificationPrismaService],
})
export class NotificationPrismaModule {}
