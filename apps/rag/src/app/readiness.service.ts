import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RABBITMQ_CONNECTION, RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ChannelModel } from 'amqplib';
import { ENV_TOKEN } from './tokens';
import type { Env } from '../config/env.schema';
import { EmbeddingDimensionProbeService } from '../embedding/embedding-dimension-probe.service';
import { RagPrismaService } from '../prisma/rag-prisma.service';

export interface ReadinessDependencyDto {
  prisma: 'ok';
  redis: 'ok';
  rabbitmq: 'ok';
  cloudinary: 'ok';
  openrouter: 'ok';
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
        this.embeddingProbe.probe(),
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
        openrouter: 'ok',
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
}
