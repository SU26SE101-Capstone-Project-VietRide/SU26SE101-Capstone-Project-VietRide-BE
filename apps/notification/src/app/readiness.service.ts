import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RABBITMQ_CONNECTION, RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { ChannelModel } from 'amqplib';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';

export interface ReadinessDependencyDto {
  prisma: 'ok';
  redis: 'ok';
  rabbitmq: 'ok';
}

export interface ReadinessDto {
  status: 'ok';
  service: 'notification';
  dependencies: ReadinessDependencyDto;
}

@Injectable()
export class ReadinessService {
  constructor(
    private readonly prisma: NotificationPrismaService,
    private readonly redis: RedisService,
    @Inject(RABBITMQ_CONNECTION) private readonly rabbitMqConnection: ChannelModel,
    private readonly topologyHealth: RabbitMqTopologyHealth,
  ) {}

  async check(): Promise<ReadinessDto> {
    try {
      await Promise.all([
        this.checkPrisma(),
        this.checkRedis(),
        this.checkRabbitMq(),
      ]);
    } catch {
      // Queue and routing key are our own constants, safe to expose; the broker's
      // message stays in the logs so readiness never leaks dependency internals.
      const failedConsumers = this.topologyHealth
        .list()
        .map(({ queue, routingKey }) => ({ queue, routingKey }));

      throw new ServiceUnavailableException({
        errorCode: 'NOTIFICATION_DEPENDENCY_UNAVAILABLE',
        detail: failedConsumers.length
          ? `${failedConsumers.length} RabbitMQ consumer(s) failed topology assertion`
          : 'Notification dependency readiness check failed',
        ...(failedConsumers.length ? { failedConsumers } : {}),
      });
    }

    return {
      status: 'ok',
      service: 'notification',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
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
}
