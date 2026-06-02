# Add a Controller Endpoint — VietRide

> How to add a new HTTP endpoint to an existing NestJS controller.
> Covers routing, auth guards, validation, response shape, and error handling.

## Prerequisites

Read `nest-conventions.md` first. The module must already exist (see `nest-scaffold-module.md`).

---

## Anatomy of a VietRide endpoint

```typescript
@Post()
// @UseGuards(...) // 1. Auth guard (if implemented)
async create(
  @Body(new ZodValidationPipe(CreateTripSchema)) dto: CreateTripDto,  // 2. Validated body
  @Req() req: RequestWithUser,                               // 3. Request context
): Promise<TripRow> {                                        // 4. Return type
  return this.tripService.create(dto, req.user.sub);         // 5. Delegate to service
}
```

---

## Step 1 — Auth Guards & Middleware

Currently, `@vietride/nest-common` does **not** export a ready-to-use `JwtAuthGuard` or `InternalJwtGuard`.
For auth, you should mirror the middleware pattern used in Gateway (`UserJwtMiddleware`), or implement the guards explicitly if the architecture allows.

If no guard is needed (public endpoint / health), leave it blank.

---

## Step 2 — Validate input with ZodValidationPipe

```typescript
// Per-param validation (preferred — explicit about which param is validated)
@Post()
async create(
  @Body(new ZodValidationPipe(CreateTripSchema)) dto: CreateTripDto,
) {}

// Query params
@Get()
async list(
  @Query(new ZodValidationPipe(ListTripsQuerySchema)) query: ListTripsQueryDto,
) {}
```

On validation failure, `ZodValidationPipe` throws a `BadRequestException`.
`ProblemDetailsExceptionFilter` (wired globally) converts it to RFC 7807 automatically.

---

## Step 3 — Access user context

```typescript
import type { RequestWithCorrelationId } from '@vietride/nest-common';

// Extend for user fields (populated by Gateway's UserJwtMiddleware)
interface RequestWithUser extends RequestWithCorrelationId {
  user?: { sub: string; role: string; operatorId?: string };
}

@Post()
async create(
  @Body(new ZodValidationPipe(CreateTripSchema)) dto: CreateTripDto,
  @Req() req: RequestWithUser,
): Promise<TripRow> {
  const userId = req.user!.sub;
  return this.tripService.create(dto, userId);
}
```

Or inject `RequestContextService` in the service layer:
```typescript
constructor(
  private readonly tripRepository: TripRepository,
  private readonly ctx: RequestContextService,
) {}

async create(dto: CreateTripDto) {
  const userId = this.ctx.userId;   // if populated by middleware
  // ...
}
```

---

## Step 4 — Throw errors correctly

```typescript
import {
  NotFoundException,
  ConflictException,
  BadRequestException,
  ForbiddenException,
  UnprocessableEntityException,
} from '@nestjs/common';

// 404
throw new NotFoundException({ errorCode: 'TRIP_NOT_FOUND', detail: `Trip ${id} not found` });

// 409
throw new ConflictException({ errorCode: 'DUPLICATE_BOOKING', detail: '...' });

// 403
throw new ForbiddenException({ errorCode: 'INSUFFICIENT_ROLE', detail: '...' });

// 422 — business rule violation (not a validation error)
throw new UnprocessableEntityException({
  errorCode: 'INVALID_STATUS_TRANSITION',
  detail: `Cannot transition from ${current} to ${next}`,
});
```

`errorCode` must be `UPPER_SNAKE_CASE`.
`ProblemDetailsExceptionFilter` handles the RFC 7807 shape — never build it manually.

---

## Step 5 — HTTP status codes

NestJS defaults: `@Post()` → 201, `@Get()` → 200, `@Put/@Patch` → 200, `@Delete` → 200.

Override when needed:
```typescript
@Post()
@HttpCode(200)   // e.g. for login endpoints
async login() {}
```

---

## Checklist

- [ ] Input validated with `ZodValidationPipe` — no class-validator, no manual checks
- [ ] Errors thrown as NestJS HTTP exceptions with `errorCode` UPPER_SNAKE_CASE
- [ ] No manual ProblemDetails object construction
- [ ] No `console.log` — use pino or Logger in service layer
- [ ] Lint and tests pass
