import { ServiceUnavailableException } from '@nestjs/common';
import { RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import { RedisService } from '@vietride/nest-redis';
import type { Channel, ChannelModel } from 'amqplib';
import { NotificationPrismaService } from '../prisma/notification-prisma.service';
import { ReadinessService } from './readiness.service';

describe('ReadinessService', () => {
  let prisma: jest.Mocked<NotificationPrismaService>;
  let redis: jest.Mocked<RedisService>;
  let channel: jest.Mocked<Channel>;
  let rabbitMqConnection: jest.Mocked<ChannelModel>;
  let topologyHealth: RabbitMqTopologyHealth;
  let service: ReadinessService;

  beforeEach(() => {
    prisma = {
      $queryRaw: jest.fn(),
    } as unknown as jest.Mocked<NotificationPrismaService>;
    redis = {
      getClient: jest.fn(() => ({ ping: jest.fn(async () => 'PONG') })),
    } as unknown as jest.Mocked<RedisService>;
    channel = {
      close: jest.fn(),
    } as unknown as jest.Mocked<Channel>;
    rabbitMqConnection = {
      createChannel: jest.fn(async () => channel),
    } as unknown as jest.Mocked<ChannelModel>;
    topologyHealth = new RabbitMqTopologyHealth();
    service = new ReadinessService(prisma, redis, rabbitMqConnection, topologyHealth);
  });

  it('returns dependency status when all checks pass', async () => {
    await expect(service.check()).resolves.toEqual({
      status: 'ok',
      service: 'notification',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
      },
    });
    expect(prisma.$queryRaw).toHaveBeenCalled();
    expect(rabbitMqConnection.createChannel).toHaveBeenCalled();
    expect(channel.close).toHaveBeenCalled();
  });

  it('throws service unavailable without leaking dependency details', async () => {
    prisma.$queryRaw.mockRejectedValue(new Error('postgres://secret@db unavailable'));

    await expect(service.check()).rejects.toThrow(ServiceUnavailableException);
    await expect(service.check()).rejects.toMatchObject({
      response: expect.objectContaining({
        errorCode: 'NOTIFICATION_DEPENDENCY_UNAVAILABLE',
      }),
    });
  });

  it('reports the queues whose consumers failed topology assertion', async () => {
    topologyHealth.record({
      queue: 'notification:trip-vehicle-substituted',
      routingKey: 'trip.trip.vehicle_substituted',
      error: "PRECONDITION_FAILED - inequivalent arg 'x-dead-letter-routing-key'",
    });

    await expect(service.check()).rejects.toMatchObject({
      response: expect.objectContaining({
        errorCode: 'NOTIFICATION_DEPENDENCY_UNAVAILABLE',
        detail: '1 RabbitMQ consumer(s) failed topology assertion',
        failedConsumers: [
          {
            queue: 'notification:trip-vehicle-substituted',
            routingKey: 'trip.trip.vehicle_substituted',
          },
        ],
      }),
    });
  });

  it('keeps the broker error message out of the readiness response', async () => {
    topologyHealth.record({
      queue: 'notification:trip-vehicle-substituted',
      routingKey: 'trip.trip.vehicle_substituted',
      error: "PRECONDITION_FAILED - inequivalent arg in vhost '/'",
    });

    const error = await service.check().catch((err: ServiceUnavailableException) => err);

    expect(JSON.stringify(error)).not.toContain('PRECONDITION_FAILED');
  });
});
