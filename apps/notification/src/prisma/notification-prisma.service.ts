import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { PrismaClient } from '../generated/notification-prisma-client';

@Injectable()
export class NotificationPrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(NotificationPrismaService.name);

  async onModuleInit(): Promise<void> {
    await this.$connect();
    this.logger.log('Notification Prisma connected');
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
    this.logger.log('Notification Prisma disconnected');
  }
}
