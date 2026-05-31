import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
// eslint-disable-next-line @nx/enforce-module-boundaries
import { PrismaClient } from '../generated/tracking-prisma-client';

@Injectable()
export class TrackingPrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(TrackingPrismaService.name);

  async onModuleInit(): Promise<void> {
    await this.$connect();
    this.logger.log('Tracking Prisma connected');
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
    this.logger.log('Tracking Prisma disconnected');
  }
}
