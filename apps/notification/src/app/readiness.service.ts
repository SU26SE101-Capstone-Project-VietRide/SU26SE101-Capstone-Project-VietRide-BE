import { Inject, Injectable, ServiceUnavailableException } from '@nestjs/common';
import { RABBITMQ_CONNECTION } from '@vietride/nest-rabbitmq';
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
  ) {}

  async check(): Promise<ReadinessDto> {
    try {
      await Promise.all([
        this.checkPrisma(),
        this.checkRedis(),
        this.checkRabbitMq(),
      ]);
    } catch {
      throw new ServiceUnavailableException({
        errorCode: 'NOTIFICATION_DEPENDENCY_UNAVAILABLE',
        detail: 'Notification dependency readiness check failed',
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
  }
}
