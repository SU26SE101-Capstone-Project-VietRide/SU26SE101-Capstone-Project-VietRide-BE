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
  ApiResponseExceptionFilter,     // wire as APP_FILTER
  ApiResponseInterceptor,         // wire as APP_INTERCEPTOR
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
  // KHÔNG import PrismaService từ lib này — mỗi service tự tạo local PrismaService
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
    { provide: APP_FILTER,      useValue: new ApiResponseExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
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
    { provide: APP_FILTER,      useValue: new ApiResponseExceptionFilter() },
    { provide: APP_INTERCEPTOR, useValue: new LoggingInterceptor() },
    { provide: APP_INTERCEPTOR, useValue: new ApiResponseInterceptor() },
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

## Database pattern — Prisma multi-schema pattern

Mỗi NestJS service có Prisma Client riêng. TUYỆT ĐỐI KHÔNG dùng shared `PrismaService` từ `@vietride/nest-persistence`.

Mỗi NestJS service dùng schema PostgreSQL riêng trùng tên service: `vietride_tracking`, `vietride_notification`, `vietride_rag`.
Trong Prisma PHẢI khai báo `schemas = ["vietride_<service>"]` và gán `@@schema("vietride_<service>")` cho từng model/enum.
Trong `db-schema/<service>/schema.sql` PHẢI `CREATE SCHEMA IF NOT EXISTS ...` và `SET search_path TO <schema>, public` trước khi tạo enum/table.

```
apps/<service>/
├── prisma/
│   └── schema.prisma              # generator output: ../src/generated/<service>-prisma-client
└── src/
    ├── generated/
    │   └── <service>-prisma-client/   # auto-generated, do NOT edit
    └── prisma/
        └── <service>-prisma.service.ts   # Local PrismaService
```

```typescript
// apps/<service>/src/prisma/<service>-prisma.service.ts
import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
// eslint-disable-next-line @nx/enforce-module-boundaries
import { PrismaClient } from '../generated/<service>-prisma-client';

@Injectable()
export class <Service>PrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(<Service>PrismaService.name);

  async onModuleInit(): Promise<void> {
    await this.$connect();
    this.logger.log('<Service> Prisma connected');
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
    this.logger.log('<Service> Prisma disconnected');
  }
}
```

```typescript
// Repository inject local service
@Injectable()
export class TripRepository {
  constructor(private readonly prisma: TripPrismaService) {}

  async findById(id: string) {
    return this.prisma.trip.findUnique({ where: { id } });
  }
}
```

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
// Throw standard NestJS exceptions — ApiResponseExceptionFilter converts automatically
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

`errorCode` must be `UPPER_SNAKE_CASE`. Never build an ApiResponse object manually.

---

## Logging pattern

```
Layer                 | Tool            | Scope
---------------------|-----------------|------------------------------------------
Infrastructure        | NestJS Logger   | Filters, interceptors, PrismaService lifecycle
Business              | pino            | Services, repositories, event consumers
```

```typescript
// Infrastructure layer — ví dụ: PrismaService lifecycle — dùng NestJS Logger
private readonly logger = new Logger(TrackingPrismaService.name);
this.logger.log('Tracking Prisma connected');

// Business layer — ví dụ: Service — dùng pino
import pino from 'pino';
const logger = pino({ name: 'TripService' });
logger.info({ tripId }, 'Creating trip');
```

**NEVER** use `console.log` anywhere.

---

## Swagger Documentation

Mọi controller và endpoint mới BẮT BUỘC phải được document bằng `@nestjs/swagger`.

```typescript
import { ApiBearerAuth, ApiOperation, ApiResponse, ApiTags, ApiParam, ApiQuery } from '@nestjs/swagger';

@ApiTags('Trips')
@ApiBearerAuth()
@Controller('trips')
export class TripController {
  @Get(':id')
  @ApiOperation({ summary: 'Get trip by ID' })
  @ApiParam({ name: 'id', format: 'uuid', description: 'Trip ID' })
  @ApiResponse({ status: 200, description: 'Success' })
  @ApiResponse({ status: 404, description: 'Trip not found' })
  async getTrip(@Param('id') id: string) {
    // ...
  }
}
```
Lưu ý: Vì project dùng Zod schemas thay cho class-validator, Swagger không thể tự động parse schema từ class. Do đó, cần khai báo rõ `@ApiParam`, `@ApiQuery`, `@ApiBody` thủ công trên các methods.

---

## Line endings

`.ts` `.json` `.md` files must use **LF**. Enforced by `.gitattributes`.
