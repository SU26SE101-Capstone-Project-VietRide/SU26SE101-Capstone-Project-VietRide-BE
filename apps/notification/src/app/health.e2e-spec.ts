import { Test, TestingModule } from '@nestjs/testing';
import { INestApplication } from '@nestjs/common';
import { HealthController } from './health.controller';
import { ReadyController } from './ready.controller';
import { ReadinessService } from './readiness.service';

describe('Notification health endpoints (e2e)', () => {
  let app: INestApplication;
  let baseUrl: string;
  let readinessService: jest.Mocked<ReadinessService>;

  beforeAll(async () => {
    readinessService = {
      check: jest.fn(),
    } as unknown as jest.Mocked<ReadinessService>;
    const moduleFixture: TestingModule = await Test.createTestingModule({
      controllers: [HealthController, ReadyController],
      providers: [{ provide: ReadinessService, useValue: readinessService }],
    }).compile();

    app = moduleFixture.createNestApplication();
    await app.listen(0);
    baseUrl = await app.getUrl();
  });

  afterAll(async () => {
    await app.close();
  });

  it('GET /health returns liveness payload', async () => {
    const response = await fetch(`${baseUrl}/health`);
    const body = await response.json();

    expect(response.status).toBe(200);
    expect(body).toEqual({ status: 'ok', service: 'notification' });
  });

  it('GET /ready returns readiness payload', async () => {
    readinessService.check.mockResolvedValue({
      status: 'ok',
      service: 'notification',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
      },
    });
    const response = await fetch(`${baseUrl}/ready`);
    const body = await response.json();

    expect(response.status).toBe(200);
    expect(body).toEqual({
      status: 'ok',
      service: 'notification',
      dependencies: {
        prisma: 'ok',
        redis: 'ok',
        rabbitmq: 'ok',
      },
    });
  });
});
