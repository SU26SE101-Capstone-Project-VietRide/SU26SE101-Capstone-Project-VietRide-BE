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

## Unit test — Repository (with real PgService mock)

```typescript
// trip.repository.spec.ts
import { Test } from '@nestjs/testing';
import { TripRepository } from './trip.repository';
import { PgService } from '@vietride/nest-persistence';

describe('TripRepository', () => {
  let repo: TripRepository;
  let pg: jest.Mocked<PgService>;

  beforeEach(async () => {
    const module = await Test.createTestingModule({
      providers: [
        TripRepository,
        {
          provide: PgService,
          useValue: { query: jest.fn() },
        },
      ],
    }).compile();

    repo = module.get(TripRepository);
    pg   = module.get(PgService);
  });

  it('returns null when row not found', async () => {
    pg.query.mockResolvedValue({ rows: [] } as any);
    await expect(repo.findById('xyz')).resolves.toBeNull();
  });
});
```

---

## Local database setup

Start all infra (Postgres, Redis, RabbitMQ):
```bash
docker compose -f infra/docker/docker-compose.yml up -d postgres redis rabbitmq
```

Check it is running:
```bash
docker compose -f infra/docker/docker-compose.yml ps
```

Stop:
```bash
docker compose -f infra/docker/docker-compose.yml down
```

Service ports:
- Postgres: `localhost:5432`
- Redis: `localhost:6379`
- RabbitMQ: `localhost:5672` (AMQP), `localhost:15672` (management UI)

---

## Database migrations — NestJS apps

NestJS services use raw SQL. Migrations are plain `.sql` files in `db-schema/<service>/`.

There is no ORM migration runner. Apply migrations manually via psql:
```bash
psql $DATABASE_URL -f db-schema/<service>/schema.sql
```

Or via the Docker container:
```bash
docker exec -i <postgres-container> psql -U postgres -d <db> < db-schema/<service>/schema.sql
```

Check `db-schema/<service>/README.md` for service-specific instructions.

**Do NOT use TypeORM migrations, Prisma migrate, or any ORM migration tool.**

---

## Checklist

- [ ] Unit tests mock all external deps (PgService, RabbitMqPublisher, etc.)
- [ ] No real DB connection in unit tests
- [ ] Test file names follow `*.spec.ts` / `*.e2e-spec.ts` pattern
- [ ] `npm run test:ts` green
