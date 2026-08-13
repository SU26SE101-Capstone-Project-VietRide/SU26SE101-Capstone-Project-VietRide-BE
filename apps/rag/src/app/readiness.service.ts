import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RABBITMQ_CONNECTION, RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ChannelModel } from 'amqplib';
import { CHAT_COMPLETION_PROVIDER, ENV_TOKEN } from './tokens';
import type { Env } from '../config/env.schema';
import { EmbeddingDimensionProbeService } from '../embedding/embedding-dimension-probe.service';
import { RagPrismaService } from '../prisma/rag-prisma.service';
import type { ChatCompletionProvider } from '../providers/chat-completion.provider';

const RAG_INGEST_BACKLOG_MAX_AGE_MS = 15 * 60 * 1_000;
const RAG_CHAT_READINESS_MESSAGES = [
  { role: 'system' as const, content: 'Reply with OK.' },
  { role: 'user' as const, content: 'VietRide readiness probe' },
];

export interface ReadinessDependencyDto {
  prisma: 'ok';
  redis: 'ok';
  rabbitmq: 'ok';
  cloudinary: 'ok';
  shopaikey: 'ok';
  ingest: 'ok';
}

export interface ReadinessDto {
  status: 'ok';
  service: 'rag';
  dependencies: ReadinessDependencyDto;
}

@Injectable()
export class ReadinessService {
  constructor(
    private readonly prisma: RagPrismaService,
    private readonly redis: RedisService,
    private readonly embeddingProbe: EmbeddingDimensionProbeService,
    @Inject(CHAT_COMPLETION_PROVIDER) private readonly chatProvider: ChatCompletionProvider,
    @Inject(ENV_TOKEN) private readonly env: Env,
    @Inject(RABBITMQ_CONNECTION) private readonly rabbitMqConnection: ChannelModel,
    private readonly topologyHealth: RabbitMqTopologyHealth,
  ) {}

  async check(): Promise<ReadinessDto> {
    try {
      await Promise.all([
        this.checkPrisma(),
        this.checkRedis(),
        this.checkRabbitMq(),
        this.checkCloudinaryConfig(),
        this.checkShopAiKey(),
        this.checkIngest(),
      ]);
    } catch {
      // Queue and routing key are our own constants, safe to expose; the broker's
      // message stays in the logs so readiness never leaks dependency internals.
      const failedConsumers = this.topologyHealth
        .list()
        .map(({ queue, routingKey }) => ({ queue, routingKey }));

      throw new ServiceUnavailableException({
        errorCode: 'RAG_DEPENDENCY_UNAVAILABLE',
        detail: failedConsumers.length
          ? `${failedConsumers.length} RabbitMQ consumer(s) failed topology assertion`
          : 'RAG dependency readiness check failed',
        ...(failedConsumers.length ? { failedConsumers } : {}),
      });
    }

    return {
      status: 'ok',
      service: 'rag',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
        cloudinary: 'ok',
        shopaikey: 'ok',
        ingest: 'ok',
      },
    };
  }

  private async checkPrisma(): Promise<void> {
    await this.prisma.$queryRaw`SELECT 1`;
  }

  private async checkRedis(): Promise<void> {
    await this.redis.getClient().ping();
  }

  private async checkRabbitMq(): Promise<void> {
    const channel = await this.rabbitMqConnection.createChannel();
    await channel.close();

    if (!this.topologyHealth.isHealthy) {
      throw new Error('RabbitMQ consumers degraded');
    }
  }

  private checkCloudinaryConfig(): void {
    if (
      !this.env.CLOUDINARY_CLOUD_NAME ||
      !this.env.CLOUDINARY_API_KEY ||
      !this.env.CLOUDINARY_API_SECRET ||
      !this.env.CLOUDINARY_RAG_FOLDER
    ) {
      throw new ServiceUnavailableException({
        errorCode: 'RAG_STORAGE_CONFIG_UNAVAILABLE',
        detail: 'RAG Cloudinary configuration is incomplete',
      });
    }
  }

  private async checkShopAiKey(): Promise<void> {
    const [, chatProbe] = await Promise.all([
      this.embeddingProbe.probe(),
      this.chatProvider.complete({
        stream: false,
        messages: RAG_CHAT_READINESS_MESSAGES,
      }),
    ]);
    if (!chatProbe.trim()) throw new Error('ShopAIKey chat probe returned no content');
  }

  private async checkIngest(): Promise<void> {
    if (!this.env.RAG_INGEST_WORKER_ENABLED) {
      throw new Error('RAG ingest worker is disabled');
    }

    const staleBefore = new Date(Date.now() - RAG_INGEST_BACKLOG_MAX_AGE_MS);
    const staleDocuments = await this.prisma.knowledgeDocument.count({
      where: {
        status: 'APPROVED',
        ingestStatus: { in: ['PENDING', 'PROCESSING'] },
        updatedAt: { lt: staleBefore },
      },
    });
    if (staleDocuments > 0) throw new Error('RAG ingest backlog is stale');
  }
}
