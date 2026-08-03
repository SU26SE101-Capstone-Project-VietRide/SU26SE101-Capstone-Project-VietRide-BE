import { ServiceUnavailableException } from '@nestjs/common';
import { RabbitMqTopologyHealth } from '@vietride/nest-rabbitmq';
import type { RedisService } from '@vietride/nest-redis';
import type { Channel, ChannelModel } from 'amqplib';
import type { TrackingPrismaService } from '../prisma/tracking-prisma.service';
import { ReadinessService } from './readiness.service';

describe('ReadinessService', () => {
  let prisma: jest.Mocked<TrackingPrismaService>;
  let redis: RedisService;
  let channel: jest.Mocked<Channel>;
  let rabbitMqConnection: jest.Mocked<ChannelModel>;
  let topologyHealth: RabbitMqTopologyHealth;
  let service: ReadinessService;

  beforeEach(() => {
    prisma = {
      $queryRaw: jest.fn(),
    } as unknown as jest.Mocked<TrackingPrismaService>;
    redis = {
      getClient: jest.fn(() => ({ ping: jest.fn(async () => 'PONG') })),
    } as unknown as RedisService;
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
      service: 'tracking',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
      },
    });
  });

  it('fails readiness and names the queue when a consumer failed topology assertion', async () => {
    topologyHealth.record({
      queue: 'tracking:trip-assigned',
      routingKey: 'trip.trip.assigned',
      error: "PRECONDITION_FAILED - inequivalent arg 'x-dead-letter-routing-key'",
    });

    await expect(service.check()).rejects.toMatchObject({
      response: expect.objectContaining({
        errorCode: 'TRACKING_DEPENDENCY_UNAVAILABLE',
        detail: '1 RabbitMQ consumer(s) failed topology assertion',
        failedConsumers: [{ queue: 'tracking:trip-assigned', routingKey: 'trip.trip.assigned' }],
      }),
    });
  });

  it('throws service unavailable without leaking dependency details', async () => {
    prisma.$queryRaw.mockRejectedValue(new Error('postgres://secret@db unavailable'));

    const error = await service.check().catch((err: ServiceUnavailableException) => err);

    expect(error).toBeInstanceOf(ServiceUnavailableException);
    expect(JSON.stringify(error)).not.toContain('secret');
  });
});
