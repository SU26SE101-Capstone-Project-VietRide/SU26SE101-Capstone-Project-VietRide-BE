# Testing & DB Setup — VietRide NestJS

> How to write unit tests and e2e tests, run them, and manage database setup locally.

## Prerequisites

Read `nest-conventions.md` first.

---

## Running tests

```bash
# Single app
npm run test:ts

# Single app — watch mode (local dev)
nx run <app>:test --watch

# Single app — E2E test
nx run <app>:test:e2e

# Single test file
nx run <app>:test --testFile=src/trip/trip.service.spec.ts
```

---

## Unit test — Service

Test services in isolation by mocking the repository and any other deps.

```typescript
// trip.service.spec.ts
import { Test } from '@nestjs/testing';
import { TripService } from './trip.service';
import { TripRepository } from './trip.repository';

describe('TripService', () => {
  let service: TripService;
  let repo: jest.Mocked<TripRepository>;

  beforeEach(async () => {
    const module = await Test.createTestingModule({
      providers: [
        TripService,
        {
          provide: TripRepository,
          useValue: {
            findById: jest.fn(),
            create:   jest.fn(),
          },
        },
      ],
    }).compile();

    service = module.get(TripService);
    repo    = module.get(TripRepository);
  });

  it('throws NotFoundException when trip does not exist', async () => {
    repo.findById.mockResolvedValue(null);
    await expect(service.findById('nonexistent-id')).rejects.toThrow('TRIP_NOT_FOUND');
  });

  it('returns trip when found', async () => {
    const trip = { id: 'abc', origin: 'HCM', destination: 'HN' } as any;
    repo.findById.mockResolvedValue(trip);
    await expect(service.findById('abc')).resolves.toEqual(trip);
  });
});
```

---

## Unit test — Repository (with PrismaService mock)

```typescript
// trip.repository.spec.ts
import { Test } from '@nestjs/testing';
import { TripRepository } from './trip.repository';
import { PrismaService } from '@vietride/nest-persistence';

describe('TripRepository', () => {
  let repo: TripRepository;
  let prisma: jest.Mocked<PrismaService>;

  beforeEach(async () => {
    const module = await Test.createTestingModule({
      providers: [
        TripRepository,
        {
          provide: PrismaService,
          useValue: {
            trip: {
              findUnique: jest.fn(),
            },
          },
        },
      ],
    }).compile();

    repo   = module.get(TripRepository);
    prisma = module.get(PrismaService);
  });

  it('returns null when row not found', async () => {
    prisma.trip.findUnique.mockResolvedValue(null);
    await expect(repo.findById('xyz')).resolves.toBeNull();
  });
});
```

---

## E2E test — Supertest

E2E test files must be placed in `test/` directory and named `*.e2e-spec.ts`.
Always test 3 cases: Happy path, Auth (401), Validation (400).

```typescript
// test/locations.e2e-spec.ts
import { Test, TestingModule } from '@nestjs/testing';
import { INestApplication } from '@nestjs/common';
import * as request from 'supertest';
import { AppModule } from './../src/app.module';
import { NestCommonModule } from '@vietride/nest-common';

describe('LocationsController (e2e)', () => {
  let app: INestApplication;

  beforeAll(async () => {
    const moduleFixture: TestingModule = await Test.createTestingModule({
      imports: [AppModule],
    }).compile();

    app = moduleFixture.createNestApplication();
    // Bắt buộc apply global pipes/filters tương tự như bootstrap thật
    // app.useGlobalPipes(new ZodValidationPipe());
    await app.init();
  });

  afterAll(async () => {
    await app.close();
  });

  it('/v1/locations (POST) - missing token -> 401', () => {
    return request(app.getHttpServer())
      .post('/v1/locations')
      .send({})
      .expect(401);
  });

  it('/v1/locations (POST) - validation failed -> 400', () => {
    return request(app.getHttpServer())
      .post('/v1/locations')
      .set('X-Internal-Auth', 'Bearer valid-token')
      .send({ lat: 'invalid' }) // thiếu lng, lat sai type
      .expect(400)
      .expect((res) => {
        expect(res.body.errorCode).toBe('VALIDATION_FAILED');
      });
  });

  it('/v1/locations (POST) - happy path -> 201', () => {
    return request(app.getHttpServer())
      .post('/v1/locations')
      .set('X-Internal-Auth', 'Bearer valid-token')
      .send({ driverId: 'd1', lat: 10.1, lng: 106.1, timestamp: Date.now() })
      .expect(201)
      .expect((res) => {
        expect(res.body.driverId).toBe('d1');
      });
  });
});
```

---

## Local database setup

Start all infra (Postgres, Redis, RabbitMQ):
```bash
docker compose -f infra/docker/docker-compose.yml up -d postgres redis rabbitmq
```

Service ports:
- Postgres: `localhost:5432`
- Redis: `localhost:6379`
- RabbitMQ: `localhost:5672` (AMQP)

---

## Database migrations — NestJS apps (Prisma)

NestJS services use Prisma ORM. Each service has its own `schema.prisma` inside `apps/<app_name>/prisma/`.

Create a migration and apply it to dev DB:
```bash
cd apps/<app_name>
npx prisma migrate dev --name init
```

Generate Prisma Client:
```bash
cd apps/<app_name>
npx prisma generate
```

---

## Checklist

- [ ] Unit tests mock all external deps (PrismaService, RabbitMqPublisher, etc.)
- [ ] No real DB connection in unit tests
- [ ] E2E tests use `supertest` and cover Happy Path + 400 + 401
- [ ] `nx run <app>:test:e2e` green
- [ ] `nx run <app>:test` green
