# NestJS Conventions — VietRide

> Source of truth for naming, folder structure, and import rules for all NestJS apps.
> All other guides reference this file. Read this first before any NestJS task.
> When in doubt about any pattern → open `apps/gateway/src/` and mirror it.

## Stack

| | Version |
|--|--|
| NestJS | 11.x |
| Node | 20 |
| Database ORM | Prisma ORM |
| Validation | zod 3.x |
| JWT | jose 5.x |
| Redis client | ioredis 5.x |
| AMQP | amqplib 0.10.x |
| Logging | pino 9.x |

**package.json is source of truth for versions — NOT BSOT §2.2.**

---

## Apps and ports

| App | Port | Role |
|-----|------|------|
| `gateway` | 3000 | Reverse proxy + JWT auth + rate limiting |
| `tracking` | 3001 | Worker — location tracking events |
| `notification` | 3002 | Worker — push / SMS / email |
| `rag` | 3003 | Worker — RAG / AI features |

---

## Folder structure per app

```
apps/<app>/src/
├── <app>.module.ts          # root module — wire NestCommonModule, NestPersistenceModule here
├── main.ts                  # bootstrap only — no business logic
├── config/
│   └── env.schema.ts        # zod env schema, extend baseEnvSchema
├── health/
│   └── health.controller.ts # GET /health + GET /ready
└── <aggregate>/             # one folder per domain aggregate
    ├── <aggregate>.module.ts
    ├── <aggregate>.controller.ts
    ├── <aggregate>.service.ts
    ├── <aggregate>.repository.ts   # Database access via Prisma ORM
    └── dto/
        └── <verb>-<aggregate>.dto.ts  # zod schema + inferred type
```

---

## Naming conventions

| Artifact | Pattern | Example |
|----------|---------|---------|
| Module | `<Aggregate>Module` | `TripModule` |
| Controller | `<Aggregate>Controller` | `TripController` |
| Service | `<Aggregate>Service` | `TripService` |
| Repository | `<Aggregate>Repository` | `TripRepository` |
| DTO schema | `<Verb><Aggregate>Schema` | `CreateTripSchema` |
| DTO type | `<Verb><Aggregate>Dto` | `CreateTripDto` |
| Env token | `ENV_TOKEN` | (always this name) |
| File | `<aggregate>.<role>.ts` | `trip.service.ts` |

One class per file. No barrel re-exports unless the lib requires it.

---

## Shared libs — what exists and what to import

### `@vietride/nest-common`
```typescript
import {
  NestCommonModule,               // wire in root AppModule.imports
  ProblemDetailsExceptionFilter,  // wire as APP_FILTER
  ZodValidationPipe,              // use per-param: @Body(new ZodValidationPipe(Schema))
  LoggingInterceptor,             // wire as APP_INTERCEPTOR
  RequestContextService,          // inject for requestId / userId / role
  CorrelationIdMiddleware,        // auto-applied by NestCommonModule
} from '@vietride/nest-common';
```
*(Note: `JwtAuthGuard` and `InternalJwtGuard` are not currently exported. If needed, create them or use middleware patterns like Gateway's `UserJwtMiddleware`)*

### `@vietride/nest-persistence`
```typescript
import {
  NestPersistenceModule,  // NestPersistenceModule.forRoot({ connectionString })
  PrismaService,          // inject for database access
} from '@vietride/nest-persistence';
```

### `@vietride/nest-rabbitmq`
```typescript
import { RabbitMqPublisher, RabbitMqConsumer } from '@vietride/nest-rabbitmq';
```

### `@vietride/nest-redis`
```typescript
import { RedisService, NestRedisModule } from '@vietride/nest-redis';
```

### `@vietride/nest-config`
```typescript
import { baseEnvSchema } from '@vietride/nest-config';
```

### `@vietride/contracts`
```typescript
import type { SomeSharedType } from '@vietride/contracts';
```

---

## Root AppModule wiring (Worker Apps vs Gateway)

### Gateway Pattern (Cross-cutting only)
```typescript
@Module({
  imports: [NestCommonModule],
  providers: [
    { provide: APP_FILTER,      useValue: new ProblemDetailsExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
  ],
})
export class AppModule {}
```

### Worker App Pattern (Tracking, Notification, RAG)
Workers connect to PostgreSQL via `NestPersistenceModule`.
```typescript
@Module({
  imports: [
    NestCommonModule,
    NestPersistenceModule.forRoot({ connectionString: env.DATABASE_URL }), // DB access
  ],
  providers: [
    { provide: APP_FILTER,      useValue: new ProblemDetailsExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
  ],
})
export class AppModule {}
```

---

## Env schema pattern

```typescript
// config/env.schema.ts
import { baseEnvSchema } from '@vietride/nest-config';
import { z } from 'zod';

export const envSchema = baseEnvSchema.merge(z.object({
  DATABASE_URL:  z.string().url(),
  RABBITMQ_URL:  z.string().url(),
  REDIS_URL:     z.string().url(),
}));

export type Env = z.infer<typeof envSchema>;
export const loadEnv = (): Env => envSchema.parse(process.env);
```

Call `loadEnv()` once at module load time (top of `app.module.ts`), store result,
inject via `ENV_TOKEN`. Never call `process.env` directly in services.

---

## Database pattern — Prisma ORM

```typescript
@Injectable()
export class TripRepository {
  constructor(private readonly prisma: PrismaService) {}

  async findById(id: string) {
    return this.prisma.trip.findUnique({
      where: { id },
    });
  }

  async create(data: CreateTripDto) {
    return this.prisma.trip.create({
      data: {
        id: randomUUID(),
        origin: data.origin,
        destination: data.destination,
      },
    });
  }
}
```

- Repository handles DB access; Service handles business logic

---

## Validation pattern — Zod

```typescript
// dto/create-trip.dto.ts
import { z } from 'zod';

export const CreateTripSchema = z.object({
  origin:      z.string().min(1),
  destination: z.string().min(1),
  scheduledAt: z.string().datetime().optional(),
});

export type CreateTripDto = z.infer<typeof CreateTripSchema>;
```

```typescript
// controller
@Post()
async create(
  @Body(new ZodValidationPipe(CreateTripSchema)) dto: CreateTripDto,
): Promise<TripRow> {
  return this.tripService.create(dto);
}
```

---

## Error throwing pattern

```typescript
// Throw standard NestJS exceptions — ProblemDetailsExceptionFilter converts automatically
throw new NotFoundException({
  errorCode: 'TRIP_NOT_FOUND',
  detail: `Trip ${id} not found`,
});

throw new ConflictException({
  errorCode: 'BOOKING_ALREADY_EXISTS',
  detail: 'A booking for this trip already exists',
});

throw new BadRequestException({
  errorCode: 'INVALID_STATUS_TRANSITION',
  detail: `Cannot transition from ${current} to ${next}`,
});
```

`errorCode` must be `UPPER_SNAKE_CASE`. Never build a ProblemDetails object manually.

---

## Logging pattern

- **HTTP request/response logging**: Handled by `LoggingInterceptor` (pino) via `APP_INTERCEPTOR`.
- **Service/Repository business logs**: Use NestJS `Logger` (like shared libs do) or `pino` (just be consistent).
- **NEVER** use `console.log`.

```typescript
import { Logger } from '@nestjs/common';

@Injectable()
export class TripService {
  private readonly logger = new Logger('TripService');

  async create(dto: CreateTripDto) {
    this.logger.log(`Creating trip...`);
    // ...
  }
}
```

---

## Line endings

`.ts` `.json` `.md` files must use **LF**. Enforced by `.gitattributes`.
