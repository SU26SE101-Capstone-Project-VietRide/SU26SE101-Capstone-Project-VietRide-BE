import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { PrismaClient } from '../generated/rag-prisma-client';

@Injectable()
export class RagPrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(RagPrismaService.name);

  async onModuleInit(): Promise<void> {
    await this.$connect();
    this.logger.log('RAG Prisma connected');
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
    this.logger.log('RAG Prisma disconnected');
  }
}
